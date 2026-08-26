using Hospital.Core.Application;
using Hospital.Core.Audit;
using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Persistence;
using Hospital.Core.Pharmacy;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Prescriptions;

public sealed class IssuePrescriptionUseCase(
    IApplicationDbContext applicationDbContext,
    IApplicationTransaction applicationTransaction,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<DoctorPrescriptionDetails>> ExecuteAsync(
        long doctorUserProfileId,
        long consultationId,
        long medicationId,
        string? dose,
        string? instructions,
        int quantity,
        string? traceId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRequest(
                consultationId,
                medicationId,
                dose,
                instructions,
                quantity,
                out string? normalizedDose,
                out string? normalizedInstructions,
                out ApplicationResult<DoctorPrescriptionDetails>? validationFailure))
        {
            return validationFailure!;
        }

        Consultation? consultation = await DoctorConsultationAccess.LoadOwnedAsync(
            applicationDbContext,
            consultationId,
            doctorUserProfileId,
            trackChanges: false,
            cancellationToken);

        if (consultation is null)
        {
            return ApplicationResult.NotFound<DoctorPrescriptionDetails>(
                "consultation_not_found",
                "The consultation was not found.");
        }

        if (consultation.Status != ConsultationStatus.Completed)
        {
            return ApplicationResult.Conflict<DoctorPrescriptionDetails>(
                "consultation_not_completed",
                "Prescriptions can only be issued from a completed consultation.");
        }

        Medication? medication = await applicationDbContext.Medications
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == medicationId, cancellationToken);
        if (medication is null)
        {
            return ApplicationResult.NotFound<DoctorPrescriptionDetails>(
                "medication_not_found",
                "The medication was not found in the local catalog.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        long prescriberClinicianProfileId = consultation.Appointment
            .AvailabilitySlot
            .ClinicianProfileId;
        long patientProfileId = consultation.Appointment.PatientProfileId;
        string? normalizedTraceId = AuditTrace.Normalize(traceId);

        long prescriptionId = await applicationTransaction.ExecuteAsync(
            async transactionCancellationToken =>
            {
                Prescription prescription = new()
                {
                    ConsultationId = consultation.Id,
                    MedicationId = medication.Id,
                    PrescriberClinicianProfileId = prescriberClinicianProfileId,
                    PatientProfileId = patientProfileId,
                    RxCuiSnapshot = medication.RxCui,
                    MedicationDisplayNameSnapshot = medication.DisplayName,
                    Dose = normalizedDose!,
                    Instructions = normalizedInstructions!,
                    Quantity = quantity,
                    Status = PrescriptionStatus.Issued,
                    IssuedAtUtc = now,
                };
                applicationDbContext.Prescriptions.Add(prescription);
                await applicationDbContext.SaveChangesAsync(transactionCancellationToken);

                Fulfillment fulfillment = new()
                {
                    PrescriptionId = prescription.Id,
                    Status = FulfillmentStatus.Pending,
                    CreatedAtUtc = now,
                };
                applicationDbContext.Fulfillments.Add(fulfillment);
                await applicationDbContext.SaveChangesAsync(transactionCancellationToken);

                applicationDbContext.AuditEvents.AddRange(
                    new AuditEvent
                    {
                        ActorUserProfileId = doctorUserProfileId,
                        Action = "PrescriptionIssued",
                        AffectedEntityType = nameof(Prescription),
                        AffectedEntityId = prescription.Id,
                        OccurredAtUtc = now,
                        TraceId = normalizedTraceId,
                        MetadataJson = "{\"to\":\"Issued\"}",
                    },
                    new AuditEvent
                    {
                        ActorUserProfileId = doctorUserProfileId,
                        Action = "FulfillmentCreated",
                        AffectedEntityType = nameof(Fulfillment),
                        AffectedEntityId = fulfillment.Id,
                        OccurredAtUtc = now,
                        TraceId = normalizedTraceId,
                        MetadataJson = "{\"to\":\"Pending\"}",
                    });
                await applicationDbContext.SaveChangesAsync(transactionCancellationToken);
                return prescription.Id;
            },
            cancellationToken);

        return await PrescriptionAccess.GetAsync(
            applicationDbContext,
            prescriptionId,
            doctorUserProfileId,
            cancellationToken);
    }

    private static bool TryNormalizeRequest(
        long consultationId,
        long medicationId,
        string? dose,
        string? instructions,
        int quantity,
        out string? normalizedDose,
        out string? normalizedInstructions,
        out ApplicationResult<DoctorPrescriptionDetails>? failure)
    {
        normalizedDose = string.IsNullOrWhiteSpace(dose) ? null : dose.Trim();
        normalizedInstructions = string.IsNullOrWhiteSpace(instructions)
            ? null
            : instructions.Trim();

        if (consultationId <= 0 || medicationId <= 0)
        {
            failure = ApplicationResult.Validation<DoctorPrescriptionDetails>(
                "invalid_prescription_reference",
                "A valid consultation and medication are required.");
            return false;
        }

        if (normalizedDose is null || normalizedDose.Length > 100)
        {
            failure = ApplicationResult.Validation<DoctorPrescriptionDetails>(
                "invalid_prescription_dose",
                "Dose is required and cannot exceed 100 characters.");
            return false;
        }

        if (normalizedInstructions is null || normalizedInstructions.Length > 1000)
        {
            failure = ApplicationResult.Validation<DoctorPrescriptionDetails>(
                "invalid_prescription_instructions",
                "Instructions are required and cannot exceed 1,000 characters.");
            return false;
        }

        if (quantity is < 1 or > 999)
        {
            failure = ApplicationResult.Validation<DoctorPrescriptionDetails>(
                "invalid_prescription_quantity",
                "Quantity must be between 1 and 999.");
            return false;
        }

        failure = null;
        return true;
    }
}
