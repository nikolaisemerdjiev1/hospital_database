using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using Hospital.Api.Authentication;
using Hospital.Api.Contracts;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class AuthenticationAuthorizationTests : IDisposable
{
    private readonly AuthenticationApiFactory factory;
    private readonly HttpClient client;

    public AuthenticationAuthorizationTests(AuthenticationDatabaseFixture database)
    {
        factory = new AuthenticationApiFactory(database.ConnectionString);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task MissingBearerTokenReturnsGenericUnauthorizedProblem()
    {
        HttpResponseMessage response = await client.GetAsync("/api/v1/identity/me");

        await AssertAuthorizationProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            expectBearerChallenge: true);
    }

    [Theory]
    [InlineData(InvalidTokenKind.Malformed)]
    [InlineData(InvalidTokenKind.WrongSignature)]
    [InlineData(InvalidTokenKind.WrongIssuer)]
    [InlineData(InvalidTokenKind.WrongAudience)]
    [InlineData(InvalidTokenKind.ExpiredBeyondClockSkew)]
    [InlineData(InvalidTokenKind.FutureNotBefore)]
    [InlineData(InvalidTokenKind.DisallowedRs384)]
    [InlineData(InvalidTokenKind.NoSignature)]
    [InlineData(InvalidTokenKind.MissingExpiration)]
    public async Task InvalidBearerTokenReturnsGenericUnauthorizedProblem(
        InvalidTokenKind tokenKind)
    {
        string token = CreateInvalidToken(tokenKind);

        HttpResponseMessage response = await SendAuthenticatedAsync(
            "/api/v1/identity/me",
            token);

        await AssertAuthorizationProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            expectBearerChallenge: true);
    }

    [Fact]
    public async Task FallbackPolicyProtectsUnannotatedEndpoints()
    {
        HttpResponseMessage missingTokenResponse = await client.GetAsync(
            "/__test/authorization/fallback");
        await AssertAuthorizationProblemAsync(
            missingTokenResponse,
            HttpStatusCode.Unauthorized,
            expectBearerChallenge: true);

        string unknownProfileToken = CreateValidToken(
            "auth-test|unknown",
            ApplicationRoles.Patient);
        HttpResponseMessage unknownProfileResponse = await SendAuthenticatedAsync(
            "/__test/authorization/fallback",
            unknownProfileToken);
        await AssertAuthorizationProblemAsync(
            unknownProfileResponse,
            HttpStatusCode.Forbidden,
            expectBearerChallenge: false);

        string patientToken = CreateValidToken(
            AuthTestIdentities.PatientSubject,
            ApplicationRoles.Patient);
        HttpResponseMessage patientResponse = await SendAuthenticatedAsync(
            "/__test/authorization/fallback",
            patientToken);
        Assert.Equal(HttpStatusCode.OK, patientResponse.StatusCode);
    }

    [Theory]
    [InlineData(IdentityDenialKind.MissingSubject)]
    [InlineData(IdentityDenialKind.MissingRole)]
    [InlineData(IdentityDenialKind.DuplicateRole)]
    [InlineData(IdentityDenialKind.UnsupportedRole)]
    [InlineData(IdentityDenialKind.CaseMismatchedRole)]
    [InlineData(IdentityDenialKind.UnknownProfile)]
    [InlineData(IdentityDenialKind.InactiveProfile)]
    [InlineData(IdentityDenialKind.DatabaseRoleMismatch)]
    [InlineData(IdentityDenialKind.MissingSubtype)]
    [InlineData(IdentityDenialKind.InvalidPatientComposition)]
    [InlineData(IdentityDenialKind.InvalidAdministratorComposition)]
    public async Task AuthenticatedIdentityWithoutOneValidLocalProfileIsForbidden(
        IdentityDenialKind denialKind)
    {
        string token = factory.CreateToken(CreateDeniedIdentityClaims(denialKind));

        HttpResponseMessage response = await SendAuthenticatedAsync(
            "/api/v1/identity/me",
            token);

        await AssertAuthorizationProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            expectBearerChallenge: false);
    }

    [Fact]
    public async Task DuplicateSubjectMakesBearerTokenInvalid()
    {
        string token = factory.CreateToken(
        [
            CreateSubjectClaim(AuthTestIdentities.PatientSubject),
            CreateSubjectClaim(AuthTestIdentities.DoctorSubject),
            CreateRoleClaim(ApplicationRoles.Patient),
        ]);

        HttpResponseMessage response = await SendAuthenticatedAsync(
            "/api/v1/identity/me",
            token);

        await AssertAuthorizationProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            expectBearerChallenge: true);
    }

    [Theory]
    [InlineData(
        AuthTestIdentities.PatientSubject,
        ApplicationRoles.Patient,
        AuthTestIdentities.PatientDisplayName)]
    [InlineData(
        AuthTestIdentities.DoctorSubject,
        ApplicationRoles.Doctor,
        AuthTestIdentities.DoctorDisplayName)]
    [InlineData(
        AuthTestIdentities.PharmacistSubject,
        ApplicationRoles.Pharmacist,
        AuthTestIdentities.PharmacistDisplayName)]
    [InlineData(
        AuthTestIdentities.AdministratorSubject,
        ApplicationRoles.Administrator,
        AuthTestIdentities.AdministratorDisplayName)]
    public async Task IdentityEndpointReturnsResolvedLocalProfile(
        string subject,
        string role,
        string expectedDisplayName)
    {
        string token = CreateValidToken(subject, role);

        HttpResponseMessage response = await SendAuthenticatedAsync(
            "/api/v1/identity/me",
            token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IdentityResponse identity = await response.Content.ReadFromJsonAsync<IdentityResponse>()
            ?? throw new InvalidOperationException("The identity response body was missing.");
        Assert.True(identity.UserProfileId > 0);
        Assert.Equal(expectedDisplayName, identity.DisplayName);
        Assert.Equal(role, identity.Role);
    }

    [Theory]
    [InlineData(AuthTestIdentities.PatientSubject, ApplicationRoles.Patient, "patient", true)]
    [InlineData(AuthTestIdentities.PatientSubject, ApplicationRoles.Patient, "doctor", false)]
    [InlineData(AuthTestIdentities.PatientSubject, ApplicationRoles.Patient, "pharmacist", false)]
    [InlineData(AuthTestIdentities.PatientSubject, ApplicationRoles.Patient, "administrator", false)]
    [InlineData(AuthTestIdentities.DoctorSubject, ApplicationRoles.Doctor, "patient", false)]
    [InlineData(AuthTestIdentities.DoctorSubject, ApplicationRoles.Doctor, "doctor", true)]
    [InlineData(AuthTestIdentities.DoctorSubject, ApplicationRoles.Doctor, "pharmacist", false)]
    [InlineData(AuthTestIdentities.DoctorSubject, ApplicationRoles.Doctor, "administrator", false)]
    [InlineData(AuthTestIdentities.PharmacistSubject, ApplicationRoles.Pharmacist, "patient", false)]
    [InlineData(AuthTestIdentities.PharmacistSubject, ApplicationRoles.Pharmacist, "doctor", false)]
    [InlineData(AuthTestIdentities.PharmacistSubject, ApplicationRoles.Pharmacist, "pharmacist", true)]
    [InlineData(AuthTestIdentities.PharmacistSubject, ApplicationRoles.Pharmacist, "administrator", false)]
    [InlineData(AuthTestIdentities.AdministratorSubject, ApplicationRoles.Administrator, "patient", false)]
    [InlineData(AuthTestIdentities.AdministratorSubject, ApplicationRoles.Administrator, "doctor", false)]
    [InlineData(AuthTestIdentities.AdministratorSubject, ApplicationRoles.Administrator, "pharmacist", false)]
    [InlineData(AuthTestIdentities.AdministratorSubject, ApplicationRoles.Administrator, "administrator", true)]
    public async Task RolePoliciesPermitOnlyTheirMatchingLocalProfile(
        string subject,
        string claimedRole,
        string policyEndpoint,
        bool shouldSucceed)
    {
        string token = CreateValidToken(subject, claimedRole);

        HttpResponseMessage response = await SendAuthenticatedAsync(
            $"/__test/authorization/{policyEndpoint}",
            token);

        HttpStatusCode expectedStatus = shouldSucceed
            ? HttpStatusCode.OK
            : HttpStatusCode.Forbidden;
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private string CreateInvalidToken(InvalidTokenKind tokenKind)
    {
        Claim[] validClaims = CreateIdentityClaims(
            AuthTestIdentities.PatientSubject,
            ApplicationRoles.Patient);
        DateTime now = DateTime.UtcNow;

        return tokenKind switch
        {
            InvalidTokenKind.Malformed => "this-is-not-a-jwt",
            InvalidTokenKind.WrongSignature => factory.CreateToken(
                validClaims,
                useUntrustedSigningKey: true),
            InvalidTokenKind.WrongIssuer => factory.CreateToken(
                validClaims,
                issuer: "https://wrong-issuer.test/"),
            InvalidTokenKind.WrongAudience => factory.CreateToken(
                validClaims,
                audience: "https://wrong-audience.test"),
            InvalidTokenKind.ExpiredBeyondClockSkew => factory.CreateToken(
                validClaims,
                notBefore: now.AddMinutes(-10),
                expires: now.AddMinutes(-5)),
            InvalidTokenKind.FutureNotBefore => factory.CreateToken(
                validClaims,
                notBefore: now.AddMinutes(5),
                expires: now.AddMinutes(10)),
            InvalidTokenKind.DisallowedRs384 => factory.CreateToken(
                validClaims,
                algorithm: SecurityAlgorithms.RsaSha384),
            InvalidTokenKind.NoSignature => factory.CreateToken(
                validClaims,
                omitSignature: true),
            InvalidTokenKind.MissingExpiration => factory.CreateToken(
                validClaims,
                omitExpiration: true),
            _ => throw new ArgumentOutOfRangeException(nameof(tokenKind), tokenKind, null),
        };
    }

    private static Claim[] CreateDeniedIdentityClaims(IdentityDenialKind denialKind) =>
        denialKind switch
        {
            IdentityDenialKind.MissingSubject =>
            [
                CreateRoleClaim(ApplicationRoles.Patient),
            ],
            IdentityDenialKind.MissingRole =>
            [
                CreateSubjectClaim(AuthTestIdentities.PatientSubject),
            ],
            IdentityDenialKind.DuplicateRole =>
            [
                CreateSubjectClaim(AuthTestIdentities.PatientSubject),
                CreateRoleClaim(ApplicationRoles.Patient),
                CreateRoleClaim(ApplicationRoles.Doctor),
            ],
            IdentityDenialKind.UnsupportedRole => CreateIdentityClaims(
                AuthTestIdentities.PatientSubject,
                "nurse"),
            IdentityDenialKind.CaseMismatchedRole => CreateIdentityClaims(
                AuthTestIdentities.PatientSubject,
                "Patient"),
            IdentityDenialKind.UnknownProfile => CreateIdentityClaims(
                "auth-test|unknown",
                ApplicationRoles.Patient),
            IdentityDenialKind.InactiveProfile => CreateIdentityClaims(
                AuthTestIdentities.InactivePatientSubject,
                ApplicationRoles.Patient),
            IdentityDenialKind.DatabaseRoleMismatch => CreateIdentityClaims(
                AuthTestIdentities.DatabaseRoleMismatchSubject,
                ApplicationRoles.Doctor),
            IdentityDenialKind.MissingSubtype => CreateIdentityClaims(
                AuthTestIdentities.MissingSubtypeSubject,
                ApplicationRoles.Doctor),
            IdentityDenialKind.InvalidPatientComposition => CreateIdentityClaims(
                AuthTestIdentities.InvalidPatientCompositionSubject,
                ApplicationRoles.Patient),
            IdentityDenialKind.InvalidAdministratorComposition => CreateIdentityClaims(
                AuthTestIdentities.InvalidAdministratorCompositionSubject,
                ApplicationRoles.Administrator),
            _ => throw new ArgumentOutOfRangeException(nameof(denialKind), denialKind, null),
        };

    private string CreateValidToken(string subject, string role) =>
        factory.CreateToken(CreateIdentityClaims(subject, role));

    private static Claim[] CreateIdentityClaims(string subject, string role) =>
    [
        CreateSubjectClaim(subject),
        CreateRoleClaim(role),
    ];

    private static Claim CreateSubjectClaim(string subject) => new("sub", subject);

    private static Claim CreateRoleClaim(string role) =>
        new(AuthenticationApiFactory.RoleClaim, role);

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        string path,
        string token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task AssertAuthorizationProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        bool expectBearerChallenge)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out JsonElement traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
        Assert.False(problem.TryGetProperty("detail", out _));

        bool hasBearerChallenge = response.Headers.WwwAuthenticate.Any(
            static value => string.Equals(
                value.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectBearerChallenge, hasBearerChallenge);
    }

    public enum InvalidTokenKind
    {
        Malformed,
        WrongSignature,
        WrongIssuer,
        WrongAudience,
        ExpiredBeyondClockSkew,
        FutureNotBefore,
        DisallowedRs384,
        NoSignature,
        MissingExpiration,
    }

    public enum IdentityDenialKind
    {
        MissingSubject,
        MissingRole,
        DuplicateRole,
        UnsupportedRole,
        CaseMismatchedRole,
        UnknownProfile,
        InactiveProfile,
        DatabaseRoleMismatch,
        MissingSubtype,
        InvalidPatientComposition,
        InvalidAdministratorComposition,
    }
}
