using Hospital.Core.Persistence;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Scheduling;

public sealed class ListCliniciansUseCase(IApplicationDbContext applicationDbContext)
{
    public async Task<IReadOnlyList<ClinicianSummary>> ExecuteAsync(
        CancellationToken cancellationToken = default) =>
        await applicationDbContext.ClinicianProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.UserProfile.Status == AccountStatus.Active &&
                profile.UserProfile.ProfileType == ProfileType.Doctor)
            .OrderBy(profile => profile.UserProfile.DisplayName)
            .Select(profile => new ClinicianSummary(
                profile.Id,
                profile.UserProfile.DisplayName,
                profile.Specialty))
            .ToArrayAsync(cancellationToken);
}
