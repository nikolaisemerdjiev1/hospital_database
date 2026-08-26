using Hospital.Core.Application;
using Hospital.Core.Audit;
using Hospital.Core.Persistence;
using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Consultations;

public sealed class CompleteConsultationUseCase(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<DoctorConsultationDetails>> ExecuteAsync(
        long userProfileId,
        long consultationId,
        string? outcome,
        string? clinicalNotes,
        string? patientSummary,
        string? careInstructions,
        uint expectedVersion,
        string? traceId,
        CancellationToken cancellationToken = default)
    {
        if (consultationId <= 0 || expectedVersion == 0)
        {
            return ApplicationResult.Validation<DoctorConsultationDetails>(
                "invalid_consultation_completion",
                "A valid consultation and expected version are required.");
        }

        if (!ConsultationContent.TryCreate(
                outcome,
                clinicalNotes,
                patientSummary,
                careInstructions,
                requireComplete: true,
                out ConsultationContent? content,
                out string? errorCode,
                out string? errorMessage))
        {
            return ApplicationResult.Validation<DoctorConsultationDetails>(
                errorCode!,
                errorMessage!);
        }

        Consultation? consultation = await DoctorConsultationAccess.LoadOwnedAsync(
            applicationDbContext,
            consultationId,
            userProfileId,
            trackChanges: true,
            cancellationToken);

        if (consultation is null)
        {
            return ApplicationResult.NotFound<DoctorConsultationDetails>(
                "consultation_not_found",
                "The consultation was not found.");
        }

        if (consultation.Version != expectedVersion)
        {
            return ApplicationResult.Conflict<DoctorConsultationDetails>(
                "consultation_changed",
                "This consultation changed. Refresh it before trying again.");
        }

        if (consultation.Status != ConsultationStatus.Draft ||
            consultation.Appointment.Status != AppointmentStatus.InProgress)
        {
            return ApplicationResult.Conflict<DoctorConsultationDetails>(
                "consultation_not_completable",
                "Only an in-progress draft consultation can be completed.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (now < consultation.StartedAtUtc)
        {
            return ApplicationResult.Conflict<DoctorConsultationDetails>(
                "consultation_time_conflict",
                "The consultation completion time cannot be before its start time.");
        }

        consultation.Outcome = content!.Outcome;
        consultation.ClinicalNotes = content.ClinicalNotes;
        consultation.PatientSummary = content.PatientSummary;
        consultation.CareInstructions = content.CareInstructions;
        consultation.Status = ConsultationStatus.Completed;
        consultation.CompletedAtUtc = now;
        consultation.Appointment.Status = AppointmentStatus.Completed;

        string? normalizedTraceId = AuditTrace.Normalize(traceId);
        applicationDbContext.AuditEvents.AddRange(
            new AuditEvent
            {
                ActorUserProfileId = userProfileId,
                Action = "ConsultationCompleted",
                AffectedEntityType = nameof(Consultation),
                AffectedEntityId = consultation.Id,
                OccurredAtUtc = now,
                TraceId = normalizedTraceId,
                MetadataJson = "{\"from\":\"Draft\",\"to\":\"Completed\"}",
            },
            new AuditEvent
            {
                ActorUserProfileId = userProfileId,
                Action = "AppointmentCompleted",
                AffectedEntityType = nameof(Appointment),
                AffectedEntityId = consultation.Appointment.Id,
                OccurredAtUtc = now,
                TraceId = normalizedTraceId,
                MetadataJson = "{\"from\":\"InProgress\",\"to\":\"Completed\"}",
            });

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await DoctorConsultationAccess.ConcurrencyConflictAsync(
                applicationDbContext,
                consultationId,
                userProfileId,
                cancellationToken);
        }

        DoctorPrescriptionSummary[] prescriptions = await DoctorPrescriptionProjection.LoadAsync(
            applicationDbContext,
            consultation.Id,
            cancellationToken);

        return ApplicationResult.Success(DoctorConsultationDetails.FromEntities(
            consultation,
            prescriptions));
    }
}
