using Hospital.Core.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Scheduling;

public sealed class ListPatientAppointmentsUseCase(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider)
{
    public async Task<SchedulingResult<AppointmentPage>> ExecuteAsync(
        long userProfileId,
        int page,
        int pageSize,
        AppointmentStatus? status,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 50)
        {
            return SchedulingResult.Validation<AppointmentPage>(
                "invalid_pagination",
                "Page must be at least 1 and pageSize must be between 1 and 50.");
        }

        long itemsToSkip = (long)(page - 1) * pageSize;
        if (itemsToSkip > int.MaxValue)
        {
            return SchedulingResult.Validation<AppointmentPage>(
                "invalid_pagination",
                "The requested page is outside the supported pagination range.");
        }

        long? patientProfileId = await applicationDbContext.PatientProfiles
            .AsNoTracking()
            .Where(profile => profile.UserProfileId == userProfileId)
            .Select(profile => (long?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (!patientProfileId.HasValue)
        {
            return SchedulingResult.NotFound<AppointmentPage>(
                "patient_profile_not_found",
                "The patient profile was not found.");
        }

        IQueryable<Appointment> query = applicationDbContext.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.PatientProfileId == patientProfileId.Value);

        if (status.HasValue)
        {
            query = query.Where(appointment => appointment.Status == status.Value);
        }

        int totalItems = await query.CountAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        AppointmentSummary[] items = await query
            .OrderBy(appointment => appointment.AvailabilitySlot.StartsAtUtc < now)
            .ThenBy(appointment =>
                appointment.AvailabilitySlot.StartsAtUtc < now
                    ? DateTimeOffset.MaxValue
                    : appointment.AvailabilitySlot.StartsAtUtc)
            .ThenByDescending(appointment =>
                appointment.AvailabilitySlot.StartsAtUtc < now
                    ? appointment.AvailabilitySlot.StartsAtUtc
                    : DateTimeOffset.MinValue)
            .ThenBy(appointment => appointment.Id)
            .Skip((int)itemsToSkip)
            .Take(pageSize)
            .Select(appointment => new AppointmentSummary(
                appointment.Id,
                appointment.AvailabilitySlot.ClinicianProfileId,
                appointment.AvailabilitySlot.ClinicianProfile.UserProfile.DisplayName,
                appointment.AvailabilitySlot.ClinicianProfile.Specialty,
                appointment.AvailabilitySlot.StartsAtUtc,
                appointment.AvailabilitySlot.EndsAtUtc,
                appointment.Reason,
                appointment.Status.ToString(),
                appointment.CancelledAtUtc,
                appointment.CancellationReason,
                appointment.Version))
            .ToArrayAsync(cancellationToken);

        int totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pageSize);

        return SchedulingResult.Success(new AppointmentPage(
            items,
            page,
            pageSize,
            totalItems,
            totalPages));
    }
}
