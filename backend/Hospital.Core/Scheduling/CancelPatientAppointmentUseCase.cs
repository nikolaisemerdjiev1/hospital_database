using Hospital.Core.Application;
using Hospital.Core.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Scheduling;

public sealed class CancelPatientAppointmentUseCase(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<AppointmentSummary>> ExecuteAsync(
        long userProfileId,
        long appointmentId,
        uint expectedVersion,
        string? targetStatus,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        string? normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason?.Length > 500)
        {
            return ApplicationResult.Validation<AppointmentSummary>(
                "invalid_cancellation_reason",
                "The cancellation reason cannot exceed 500 characters.");
        }

        if (!string.Equals(
                targetStatus,
                nameof(AppointmentStatus.Cancelled),
                StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationResult.Conflict<AppointmentSummary>(
                "unsupported_transition",
                "Patients can only cancel appointments in this workflow.");
        }

        if (appointmentId <= 0 || expectedVersion == 0)
        {
            return ApplicationResult.Validation<AppointmentSummary>(
                "invalid_transition_request",
                "A valid appointment and version are required.");
        }

        Appointment? appointment = await applicationDbContext.Appointments
            .Include(candidate => candidate.AvailabilitySlot)
                .ThenInclude(slot => slot.ClinicianProfile)
                    .ThenInclude(profile => profile.UserProfile)
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == appointmentId &&
                    candidate.PatientProfile.UserProfileId == userProfileId,
                cancellationToken);

        if (appointment is null)
        {
            return ApplicationResult.NotFound<AppointmentSummary>(
                "appointment_not_found",
                "The appointment was not found.");
        }

        if (appointment.Version != expectedVersion)
        {
            return ApplicationResult.Conflict<AppointmentSummary>(
                "appointment_changed",
                "This appointment changed. Refresh it before trying again.");
        }

        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            return ApplicationResult.Conflict<AppointmentSummary>(
                "appointment_not_cancellable",
                "Only scheduled appointments can be cancelled.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (appointment.AvailabilitySlot.StartsAtUtc <= now)
        {
            return ApplicationResult.Conflict<AppointmentSummary>(
                "appointment_started",
                "An appointment cannot be cancelled after it starts.");
        }

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAtUtc = now;
        appointment.CancellationReason = normalizedReason;

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await applicationDbContext.Appointments
                .AsNoTracking()
                .Where(candidate =>
                    candidate.Id == appointmentId &&
                    candidate.PatientProfile.UserProfileId == userProfileId)
                .Select(candidate => new
                {
                    candidate.Status,
                    candidate.Version,
                })
                .SingleOrDefaultAsync(cancellationToken);

            return current is null
                ? ApplicationResult.NotFound<AppointmentSummary>(
                    "appointment_not_found",
                    "The appointment was not found.")
                : ApplicationResult.Conflict<AppointmentSummary>(
                    "appointment_changed",
                    $"This appointment is now {current.Status} at version {current.Version}. Refresh it before trying again.");
        }

        return ApplicationResult.Success(ToSummary(appointment));
    }

    private static AppointmentSummary ToSummary(Appointment appointment) =>
        new(
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
            appointment.Version);
}
