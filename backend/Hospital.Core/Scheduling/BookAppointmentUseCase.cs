using Hospital.Core.Application;
using Hospital.Core.Persistence;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Scheduling;

public sealed class BookAppointmentUseCase(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<AppointmentSummary>> ExecuteAsync(
        long userProfileId,
        long availabilitySlotId,
        uint expectedAvailabilityVersion,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        string normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length is < 1 or > 500)
        {
            return ApplicationResult.Validation<AppointmentSummary>(
                "invalid_appointment_reason",
                "The appointment reason must contain between 1 and 500 characters.");
        }

        if (availabilitySlotId <= 0 || expectedAvailabilityVersion == 0)
        {
            return ApplicationResult.Validation<AppointmentSummary>(
                "invalid_booking_request",
                "A valid availability slot and version are required.");
        }

        var patient = await applicationDbContext.PatientProfiles
            .AsNoTracking()
            .Where(profile => profile.UserProfileId == userProfileId)
            .Select(profile => new
            {
                profile.Id,
                profile.UserProfile.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (patient is null || patient.Status != AccountStatus.Active)
        {
            return ApplicationResult.NotFound<AppointmentSummary>(
                "patient_profile_not_found",
                "The patient profile was not found.");
        }

        AvailabilitySlot? slot = await applicationDbContext.AvailabilitySlots
            .Include(candidate => candidate.ClinicianProfile)
                .ThenInclude(profile => profile.UserProfile)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == availabilitySlotId,
                cancellationToken);

        if (slot is null ||
            slot.ClinicianProfile.UserProfile.Status != AccountStatus.Active ||
            slot.ClinicianProfile.UserProfile.ProfileType != ProfileType.Doctor)
        {
            return ApplicationResult.NotFound<AppointmentSummary>(
                "availability_not_found",
                "The availability slot was not found.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (slot.StartsAtUtc <= now)
        {
            return ApplicationResult.Conflict<AppointmentSummary>(
                "availability_started",
                "This appointment time has already started.");
        }

        if (slot.Version != expectedAvailabilityVersion)
        {
            return ApplicationResult.Conflict<AppointmentSummary>(
                "availability_changed",
                "This appointment time changed. Refresh the available times and try again.");
        }

        bool slotAlreadyBooked = await applicationDbContext.Appointments
            .AsNoTracking()
            .AnyAsync(
                appointment =>
                    appointment.AvailabilitySlotId == slot.Id &&
                    appointment.Status != AppointmentStatus.Cancelled,
                cancellationToken);

        if (slotAlreadyBooked)
        {
            return ApplicationResult.Conflict<AppointmentSummary>(
                "availability_taken",
                "This appointment time is no longer available.");
        }

        bool patientHasOverlap = await applicationDbContext.Appointments
            .AsNoTracking()
            .AnyAsync(
                appointment =>
                    appointment.PatientProfileId == patient.Id &&
                    appointment.Status != AppointmentStatus.Cancelled &&
                    appointment.AvailabilitySlot.StartsAtUtc < slot.EndsAtUtc &&
                    appointment.AvailabilitySlot.EndsAtUtc > slot.StartsAtUtc,
                cancellationToken);

        if (patientHasOverlap)
        {
            return ApplicationResult.Conflict<AppointmentSummary>(
                "appointment_overlap",
                "You already have an appointment during this time.");
        }

        Appointment appointment = new()
        {
            PatientProfileId = patient.Id,
            AvailabilitySlotId = slot.Id,
            Reason = normalizedReason,
            Status = AppointmentStatus.Scheduled,
            CreatedAtUtc = now,
        };

        applicationDbContext.Appointments.Add(appointment);

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            bool competingBookingExists = await applicationDbContext.Appointments
                .AsNoTracking()
                .AnyAsync(
                    candidate =>
                        candidate.AvailabilitySlotId == slot.Id &&
                        candidate.Status != AppointmentStatus.Cancelled,
                    cancellationToken);

            if (competingBookingExists)
            {
                return ApplicationResult.Conflict<AppointmentSummary>(
                    "availability_taken",
                    "This appointment time was just booked by someone else.");
            }

            throw;
        }

        return ApplicationResult.Success(new AppointmentSummary(
            appointment.Id,
            slot.ClinicianProfileId,
            slot.ClinicianProfile.UserProfile.DisplayName,
            slot.ClinicianProfile.Specialty,
            slot.StartsAtUtc,
            slot.EndsAtUtc,
            appointment.Reason,
            appointment.Status.ToString(),
            appointment.CancelledAtUtc,
            appointment.CancellationReason,
            appointment.Version));
    }
}
