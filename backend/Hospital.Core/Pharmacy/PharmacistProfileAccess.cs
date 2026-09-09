using Hospital.Core.Persistence;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Pharmacy;

internal static class PharmacistProfileAccess
{
    public static Task<long?> ResolveActiveProfileIdAsync(
        IApplicationDbContext applicationDbContext,
        long userProfileId,
        CancellationToken cancellationToken) =>
        applicationDbContext.PharmacistProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.UserProfileId == userProfileId &&
                profile.UserProfile.Status == AccountStatus.Active &&
                profile.UserProfile.ProfileType == ProfileType.Pharmacist)
            .Select(profile => (long?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
