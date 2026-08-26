using Hospital.Core.Application;
using Hospital.Core.Persistence;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Scheduling;

public sealed class ListClinicianAvailabilityUseCase(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(31);

    public async Task<ApplicationResult<IReadOnlyList<AvailabilitySummary>>> ExecuteAsync(
        long clinicianProfileId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (clinicianProfileId <= 0)
        {
            return ApplicationResult.NotFound<IReadOnlyList<AvailabilitySummary>>(
                "clinician_not_found",
                "The clinician was not found.");
        }

        if (to <= from || to - from > MaximumWindow)
        {
            return ApplicationResult.Validation<IReadOnlyList<AvailabilitySummary>>(
                "invalid_availability_window",
                "The availability window must end after it starts and cannot exceed 31 days.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (to <= now)
        {
            return ApplicationResult.Validation<IReadOnlyList<AvailabilitySummary>>(
                "availability_window_in_past",
                "The availability window must include future time.");
        }

        bool clinicianExists = await applicationDbContext.ClinicianProfiles
            .AsNoTracking()
            .AnyAsync(
                profile =>
                    profile.Id == clinicianProfileId &&
                    profile.UserProfile.Status == AccountStatus.Active &&
                    profile.UserProfile.ProfileType == ProfileType.Doctor,
                cancellationToken);

        if (!clinicianExists)
        {
            return ApplicationResult.NotFound<IReadOnlyList<AvailabilitySummary>>(
                "clinician_not_found",
                "The clinician was not found.");
        }

        DateTimeOffset effectiveFrom = from > now ? from : now;
        AvailabilitySummary[] slots = await applicationDbContext.AvailabilitySlots
            .AsNoTracking()
            .Where(slot =>
                slot.ClinicianProfileId == clinicianProfileId &&
                slot.StartsAtUtc >= effectiveFrom &&
                slot.StartsAtUtc < to &&
                !applicationDbContext.Appointments.Any(appointment =>
                    appointment.AvailabilitySlotId == slot.Id &&
                    appointment.Status != AppointmentStatus.Cancelled))
            .OrderBy(slot => slot.StartsAtUtc)
            .Select(slot => new AvailabilitySummary(
                slot.Id,
                slot.StartsAtUtc,
                slot.EndsAtUtc,
                slot.Version))
            .ToArrayAsync(cancellationToken);

        return ApplicationResult.Success<IReadOnlyList<AvailabilitySummary>>(slots);
    }
}
