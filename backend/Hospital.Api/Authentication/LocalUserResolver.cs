using System.Security.Claims;

using Hospital.Core.Persistence;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hospital.Api.Authentication;

public interface ILocalUserResolver
{
    Task<ResolvedLocalUser?> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}

internal sealed partial class LocalUserResolver(
    IApplicationDbContext applicationDbContext,
    IOptions<Auth0Options> auth0Options,
    ILogger<LocalUserResolver> logger) : ILocalUserResolver
{
    private ClaimsPrincipal? cachedPrincipal;
    private Task<ResolvedLocalUser?>? cachedResolution;

    public Task<ResolvedLocalUser?> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (ReferenceEquals(cachedPrincipal, principal) && cachedResolution is not null)
        {
            return cachedResolution;
        }

        cachedPrincipal = principal;
        cachedResolution = ResolveCoreAsync(principal, cancellationToken);
        return cachedResolution;
    }

    private async Task<ResolvedLocalUser?> ResolveCoreAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        string[] subjects = principal
            .FindAll("sub")
            .Select(static claim => claim.Value)
            .ToArray();

        if (subjects.Length != 1 ||
            string.IsNullOrWhiteSpace(subjects[0]) ||
            subjects[0] != subjects[0].Trim() ||
            subjects[0].Length > 255)
        {
            LogResolutionDenied(logger, "invalid-subject-claim");
            return null;
        }

        string roleClaimType = auth0Options.Value.RoleClaim;
        string[] roles = principal
            .FindAll(roleClaimType)
            .Select(static claim => claim.Value)
            .ToArray();

        if (roles.Length != 1 ||
            !ApplicationRoles.TryGetProfileType(roles[0], out ProfileType tokenProfileType))
        {
            LogResolutionDenied(logger, "invalid-role-claim");
            return null;
        }

        string subject = subjects[0];
        var profile = await applicationDbContext.UserProfiles
            .AsNoTracking()
            .Where(userProfile => userProfile.Auth0Subject == subject)
            .Select(userProfile => new
            {
                userProfile.Id,
                userProfile.DisplayName,
                userProfile.ProfileType,
                userProfile.Status,
                PatientProfileCount = applicationDbContext.PatientProfiles.Count(
                    patientProfile => patientProfile.UserProfileId == userProfile.Id),
                ClinicianProfileCount = applicationDbContext.ClinicianProfiles.Count(
                    clinicianProfile => clinicianProfile.UserProfileId == userProfile.Id),
                PharmacistProfileCount = applicationDbContext.PharmacistProfiles.Count(
                    pharmacistProfile => pharmacistProfile.UserProfileId == userProfile.Id),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            LogResolutionDenied(logger, "unknown-local-profile");
            return null;
        }

        if (profile.Status != AccountStatus.Active)
        {
            LogResolutionDenied(logger, "inactive-local-profile");
            return null;
        }

        if (profile.ProfileType != tokenProfileType)
        {
            LogResolutionDenied(logger, "role-profile-mismatch");
            return null;
        }

        bool hasExpectedSubtype = profile.ProfileType switch
        {
            ProfileType.Patient =>
                profile.PatientProfileCount == 1 &&
                profile.ClinicianProfileCount == 0 &&
                profile.PharmacistProfileCount == 0,
            ProfileType.Doctor =>
                profile.PatientProfileCount == 0 &&
                profile.ClinicianProfileCount == 1 &&
                profile.PharmacistProfileCount == 0,
            ProfileType.Pharmacist =>
                profile.PatientProfileCount == 0 &&
                profile.ClinicianProfileCount == 0 &&
                profile.PharmacistProfileCount == 1,
            ProfileType.Administrator =>
                profile.PatientProfileCount == 0 &&
                profile.ClinicianProfileCount == 0 &&
                profile.PharmacistProfileCount == 0,
            _ => false,
        };

        if (!hasExpectedSubtype)
        {
            LogResolutionDenied(logger, "invalid-local-profile-composition");
            return null;
        }

        return new ResolvedLocalUser(
            profile.Id,
            profile.DisplayName,
            profile.ProfileType);
    }

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Warning,
        Message = "Authenticated identity was denied local profile resolution: {Reason}")]
    private static partial void LogResolutionDenied(ILogger logger, string reason);
}
