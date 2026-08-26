using Hospital.Core.Application;
using Hospital.Core.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Consultations;

public sealed class SaveConsultationDraftUseCase(IApplicationDbContext applicationDbContext)
{
    public async Task<ApplicationResult<DoctorConsultationDetails>> ExecuteAsync(
        long userProfileId,
        long consultationId,
        string? outcome,
        string? clinicalNotes,
        string? patientSummary,
        string? careInstructions,
        uint expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (consultationId <= 0 || expectedVersion == 0)
        {
            return ApplicationResult.Validation<DoctorConsultationDetails>(
                "invalid_consultation_update",
                "A valid consultation and expected version are required.");
        }

        if (!ConsultationContent.TryCreate(
                outcome,
                clinicalNotes,
                patientSummary,
                careInstructions,
                requireComplete: false,
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

        if (consultation.Status != ConsultationStatus.Draft)
        {
            return ApplicationResult.Conflict<DoctorConsultationDetails>(
                "consultation_not_editable",
                "Only a draft consultation can be edited.");
        }

        consultation.Outcome = content!.Outcome;
        consultation.ClinicalNotes = content.ClinicalNotes;
        consultation.PatientSummary = content.PatientSummary;
        consultation.CareInstructions = content.CareInstructions;

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
