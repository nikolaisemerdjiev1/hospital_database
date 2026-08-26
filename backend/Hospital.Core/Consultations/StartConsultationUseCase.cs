using Hospital.Core.Application;
using Hospital.Core.Audit;
using Hospital.Core.Persistence;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Consultations;

public sealed class StartConsultationUseCase(
    IApplicationDbContext applicationDbContext,
    IApplicationTransaction applicationTransaction,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<DoctorConsultationDetails>> ExecuteAsync(
        long userProfileId,
        long appointmentId,
        uint expectedAppointmentVersion,
        string? traceId,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId <= 0 || expectedAppointmentVersion == 0)
        {
            return ApplicationResult.Validation<DoctorConsultationDetails>(
                "invalid_consultation_start",
                "A valid appointment and expected version are required.");
        }

        Appointment? appointment = await applicationDbContext.Appointments
            .Include(candidate => candidate.PatientProfile)
                .ThenInclude(profile => profile.UserProfile)
            .Include(candidate => candidate.AvailabilitySlot)
                .ThenInclude(slot => slot.ClinicianProfile)
                    .ThenInclude(profile => profile.UserProfile)
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == appointmentId &&
                    candidate.AvailabilitySlot.ClinicianProfile.UserProfileId == userProfileId &&
                    candidate.AvailabilitySlot.ClinicianProfile.UserProfile.Status ==
                        AccountStatus.Active &&
                    candidate.AvailabilitySlot.ClinicianProfile.UserProfile.ProfileType ==
                        ProfileType.Doctor,
                cancellationToken);

        if (appointment is null)
        {
            return ApplicationResult.NotFound<DoctorConsultationDetails>(
                "appointment_not_found",
                "The appointment was not found.");
        }

        if (appointment.Version != expectedAppointmentVersion)
        {
            return ApplicationResult.Conflict<DoctorConsultationDetails>(
                "appointment_changed",
                "This appointment changed. Refresh the worklist before trying again.");
        }

        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            return ApplicationResult.Conflict<DoctorConsultationDetails>(
                "appointment_not_startable",
                "Only a scheduled appointment can begin a consultation.");
        }

        bool consultationExists = await applicationDbContext.Consultations
            .AsNoTracking()
            .AnyAsync(
                consultation => consultation.AppointmentId == appointment.Id,
                cancellationToken);

        if (consultationExists)
        {
            return ApplicationResult.Conflict<DoctorConsultationDetails>(
                "consultation_already_exists",
                "This appointment already has a consultation.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Consultation consultation = new()
        {
            AppointmentId = appointment.Id,
            Status = ConsultationStatus.Draft,
            StartedAtUtc = now,
            CreatedAtUtc = now,
        };

        applicationDbContext.Consultations.Add(consultation);
        appointment.Status = AppointmentStatus.InProgress;

        try
        {
            await applicationTransaction.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    await applicationDbContext.SaveChangesAsync(transactionCancellationToken);

                    applicationDbContext.AuditEvents.Add(new AuditEvent
                    {
                        ActorUserProfileId = userProfileId,
                        Action = "ConsultationStarted",
                        AffectedEntityType = nameof(Consultation),
                        AffectedEntityId = consultation.Id,
                        OccurredAtUtc = now,
                        TraceId = AuditTrace.Normalize(traceId),
                        MetadataJson = "{\"appointmentStatus\":\"InProgress\"}",
                    });

                    await applicationDbContext.SaveChangesAsync(transactionCancellationToken);
                    return consultation.Id;
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await CurrentAppointmentConflictAsync(
                userProfileId,
                appointmentId,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            bool competingConsultationExists = await applicationDbContext.Consultations
                .AsNoTracking()
                .AnyAsync(
                    candidate => candidate.AppointmentId == appointmentId,
                    cancellationToken);

            if (competingConsultationExists)
            {
                return ApplicationResult.Conflict<DoctorConsultationDetails>(
                    "consultation_already_exists",
                    "This appointment was just started in another session. Refresh the worklist.");
            }

            throw;
        }

        return await DoctorConsultationAccess.LoadDetailsAsync(
            applicationDbContext,
            consultation.Id,
            userProfileId,
            cancellationToken);
    }

    private async Task<ApplicationResult<DoctorConsultationDetails>> CurrentAppointmentConflictAsync(
        long userProfileId,
        long appointmentId,
        CancellationToken cancellationToken)
    {
        var current = await applicationDbContext.Appointments
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == appointmentId &&
                candidate.AvailabilitySlot.ClinicianProfile.UserProfileId == userProfileId)
            .Select(candidate => new
            {
                candidate.Status,
                candidate.Version,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return current is null
            ? ApplicationResult.NotFound<DoctorConsultationDetails>(
                "appointment_not_found",
                "The appointment was not found.")
            : ApplicationResult.Conflict<DoctorConsultationDetails>(
                "appointment_changed",
                $"This appointment is now {current.Status} at version {current.Version}. Refresh the worklist before trying again.");
    }
}
