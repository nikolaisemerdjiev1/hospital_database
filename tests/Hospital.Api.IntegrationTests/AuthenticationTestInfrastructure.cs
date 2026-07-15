using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

using Hospital.Core.Profiles;
using Hospital.Infrastructure.Persistence;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Hospital.Api.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuthenticationDatabaseTestGroup
    : ICollectionFixture<AuthenticationDatabaseFixture>
{
    public const string Name = "Authentication PostgreSQL database";
}

public sealed class AuthenticationDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlDatabaseFixture database = new();

    public string ConnectionString => database.ConnectionString;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        await database.EnsureMigratedAsync();
        await SeedProfilesAsync();
    }

    public Task DisposeAsync() => database.DisposeAsync();

    private async Task SeedProfilesAsync()
    {
        DateTimeOffset createdAtUtc = new(2030, 1, 15, 8, 0, 0, TimeSpan.Zero);

        UserProfile patient = CreateUser(
            AuthTestIdentities.PatientSubject,
            AuthTestIdentities.PatientDisplayName,
            ProfileType.Patient,
            AccountStatus.Active,
            createdAtUtc);
        UserProfile doctor = CreateUser(
            AuthTestIdentities.DoctorSubject,
            AuthTestIdentities.DoctorDisplayName,
            ProfileType.Doctor,
            AccountStatus.Active,
            createdAtUtc);
        UserProfile pharmacist = CreateUser(
            AuthTestIdentities.PharmacistSubject,
            AuthTestIdentities.PharmacistDisplayName,
            ProfileType.Pharmacist,
            AccountStatus.Active,
            createdAtUtc);
        UserProfile administrator = CreateUser(
            AuthTestIdentities.AdministratorSubject,
            AuthTestIdentities.AdministratorDisplayName,
            ProfileType.Administrator,
            AccountStatus.Active,
            createdAtUtc);
        UserProfile inactivePatient = CreateUser(
            AuthTestIdentities.InactivePatientSubject,
            "Inactive Patient",
            ProfileType.Patient,
            AccountStatus.Inactive,
            createdAtUtc);
        UserProfile databaseRoleMismatch = CreateUser(
            AuthTestIdentities.DatabaseRoleMismatchSubject,
            "Database Role Mismatch",
            ProfileType.Patient,
            AccountStatus.Active,
            createdAtUtc);
        UserProfile missingSubtype = CreateUser(
            AuthTestIdentities.MissingSubtypeSubject,
            "Doctor Missing Subtype",
            ProfileType.Doctor,
            AccountStatus.Active,
            createdAtUtc);
        UserProfile invalidPatientComposition = CreateUser(
            AuthTestIdentities.InvalidPatientCompositionSubject,
            "Patient With Extra Subtype",
            ProfileType.Patient,
            AccountStatus.Active,
            createdAtUtc);
        UserProfile invalidAdministratorComposition = CreateUser(
            AuthTestIdentities.InvalidAdministratorCompositionSubject,
            "Administrator With Subtype",
            ProfileType.Administrator,
            AccountStatus.Active,
            createdAtUtc);

        await using ApplicationDbContext context = database.CreateContext();
        context.UserProfiles.AddRange(
            patient,
            doctor,
            pharmacist,
            administrator,
            inactivePatient,
            databaseRoleMismatch,
            missingSubtype,
            invalidPatientComposition,
            invalidAdministratorComposition);
        await context.SaveChangesAsync();

        context.PatientProfiles.AddRange(
            CreatePatientProfile(patient.Id, "AUTH-MRN-001", createdAtUtc),
            CreatePatientProfile(inactivePatient.Id, "AUTH-MRN-002", createdAtUtc),
            CreatePatientProfile(databaseRoleMismatch.Id, "AUTH-MRN-003", createdAtUtc),
            CreatePatientProfile(invalidPatientComposition.Id, "AUTH-MRN-004", createdAtUtc));
        context.ClinicianProfiles.AddRange(
            CreateClinicianProfile(doctor.Id, "AUTH-DOC-001", createdAtUtc),
            CreateClinicianProfile(
                invalidPatientComposition.Id,
                "AUTH-DOC-002",
                createdAtUtc));
        context.PharmacistProfiles.AddRange(
            CreatePharmacistProfile(pharmacist.Id, "AUTH-PHR-001", createdAtUtc),
            CreatePharmacistProfile(
                invalidAdministratorComposition.Id,
                "AUTH-PHR-002",
                createdAtUtc));
        await context.SaveChangesAsync();
    }

    private static UserProfile CreateUser(
        string subject,
        string displayName,
        ProfileType profileType,
        AccountStatus status,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            Auth0Subject = subject,
            DisplayName = displayName,
            ProfileType = profileType,
            Status = status,
            CreatedAtUtc = createdAtUtc,
        };

    private static PatientProfile CreatePatientProfile(
        long userProfileId,
        string medicalRecordNumber,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            UserProfileId = userProfileId,
            MedicalRecordNumber = medicalRecordNumber,
            DateOfBirth = new DateOnly(1990, 1, 1),
            CreatedAtUtc = createdAtUtc,
        };

    private static ClinicianProfile CreateClinicianProfile(
        long userProfileId,
        string staffIdentifier,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            UserProfileId = userProfileId,
            StaffIdentifier = staffIdentifier,
            Specialty = "Test Medicine",
            CreatedAtUtc = createdAtUtc,
        };

    private static PharmacistProfile CreatePharmacistProfile(
        long userProfileId,
        string staffIdentifier,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            UserProfileId = userProfileId,
            StaffIdentifier = staffIdentifier,
            PharmacyName = "Test Pharmacy",
            CreatedAtUtc = createdAtUtc,
        };
}

public sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    public const string Audience = "https://hospital-coordination-api-test";
    public const string Issuer = "https://auth.test.local/";
    public const string RoleClaim = "https://hospital.test/claims/app_role";

    private readonly RSA signingRsa = RSA.Create(2048);
    private readonly string connectionString;
    private bool disposed;

    public AuthenticationApiFactory(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public string CreateToken(
        IEnumerable<Claim> claims,
        string? issuer = null,
        string? audience = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        string algorithm = SecurityAlgorithms.RsaSha256,
        bool useUntrustedSigningKey = false,
        bool omitSignature = false,
        bool omitExpiration = false)
    {
        DateTime now = DateTime.UtcNow;
        DateTime effectiveNotBefore = notBefore ?? now.AddMinutes(-1);
        DateTime effectiveExpiration = expires ?? now.AddMinutes(5);

        if (useUntrustedSigningKey)
        {
            using RSA untrustedRsa = RSA.Create(2048);
            return CreateTokenCore(
                claims,
                issuer,
                audience,
                effectiveNotBefore,
                omitExpiration ? null : effectiveExpiration,
                algorithm,
                untrustedRsa,
                omitSignature);
        }

        return CreateTokenCore(
            claims,
            issuer,
            audience,
            effectiveNotBefore,
            omitExpiration ? null : effectiveExpiration,
            algorithm,
            signingRsa,
            omitSignature);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Auth0:Audience", Audience);
        builder.UseSetting("Authentication:Auth0:Domain", "auth.test.local");
        builder.UseSetting("Authentication:Auth0:RoleClaim", RoleClaim);
        builder.UseSetting("Frontend:Origin", "http://localhost:5173");
        builder.UseSetting("ConnectionStrings:HospitalDatabase", connectionString);
        builder.ConfigureTestServices(services =>
        {
            services
                .AddControllers()
                .AddApplicationPart(typeof(TestAuthorizationController).Assembly);

            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    RsaSecurityKey signingKey = new(signingRsa)
                    {
                        KeyId = "authentication-integration-tests",
                    };
                    OpenIdConnectConfiguration configuration = new()
                    {
                        Issuer = Issuer,
                    };
                    configuration.SigningKeys.Add(signingKey);

                    options.Configuration = configuration;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.RefreshOnIssuerKeyNotFound = false;
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                    options.TokenValidationParameters.ValidAudience = Audience;
                });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            signingRsa.Dispose();
            disposed = true;
        }

        base.Dispose(disposing);
    }

    private static string CreateTokenCore(
        IEnumerable<Claim> claims,
        string? issuer,
        string? audience,
        DateTime notBefore,
        DateTime? expires,
        string algorithm,
        RSA rsa,
        bool omitSignature)
    {
        RsaSecurityKey signingKey = new(rsa)
        {
            KeyId = "authentication-integration-tests",
        };
        SigningCredentials? signingCredentials = omitSignature
            ? null
            : new SigningCredentials(signingKey, algorithm);
        JwtSecurityToken token = new(
            issuer ?? Issuer,
            audience ?? Audience,
            claims,
            notBefore,
            expires,
            signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

internal static class AuthTestIdentities
{
    public const string PatientSubject = "auth-test|patient";
    public const string DoctorSubject = "auth-test|doctor";
    public const string PharmacistSubject = "auth-test|pharmacist";
    public const string AdministratorSubject = "auth-test|administrator";
    public const string InactivePatientSubject = "auth-test|inactive-patient";
    public const string DatabaseRoleMismatchSubject = "auth-test|database-role-mismatch";
    public const string MissingSubtypeSubject = "auth-test|missing-subtype";
    public const string InvalidPatientCompositionSubject =
        "auth-test|invalid-patient-composition";
    public const string InvalidAdministratorCompositionSubject =
        "auth-test|invalid-administrator-composition";

    public const string PatientDisplayName = "Authentication Patient";
    public const string DoctorDisplayName = "Authentication Doctor";
    public const string PharmacistDisplayName = "Authentication Pharmacist";
    public const string AdministratorDisplayName = "Authentication Administrator";
}
