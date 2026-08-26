using Hospital.Api.Contracts;
using Hospital.Core.Consultations;
using Hospital.Core.Prescriptions;

namespace Hospital.Api.Controllers;

public abstract class ClinicalControllerBase : ApplicationControllerBase
{
    protected static DoctorConsultationResponse ToResponse(
        DoctorConsultationDetails consultation) =>
        new(
            consultation.Id,
            consultation.Status,
            consultation.StartedAtUtc,
            consultation.CompletedAtUtc,
            consultation.Version,
            new DoctorAppointmentContextResponse(
                consultation.Appointment.Id,
                consultation.Appointment.StartsAtUtc,
                consultation.Appointment.EndsAtUtc,
                consultation.Appointment.Reason,
                consultation.Appointment.Status,
                consultation.Appointment.Version),
            new DoctorPatientContextResponse(
                consultation.Patient.DisplayName,
                consultation.Patient.MedicalRecordNumber,
                consultation.Patient.DateOfBirth,
                consultation.Patient.AllergySummary),
            consultation.Outcome,
            consultation.ClinicalNotes,
            consultation.PatientSummary,
            consultation.CareInstructions,
            consultation.Prescriptions.Select(static prescription =>
                new DoctorPrescriptionSummaryResponse(
                    prescription.Id,
                    prescription.RxCui,
                    prescription.MedicationDisplayName,
                    prescription.Dose,
                    prescription.Instructions,
                    prescription.Quantity,
                    prescription.Status,
                    prescription.IssuedAtUtc,
                    prescription.CancelledAtUtc,
                    prescription.FulfillmentStatus,
                    prescription.Version))
                .ToArray());

    protected static DoctorPrescriptionResponse ToResponse(
        DoctorPrescriptionDetails prescription) =>
        new(
            prescription.Id,
            prescription.ConsultationId,
            prescription.MedicationId,
            prescription.RxCui,
            prescription.MedicationDisplayName,
            prescription.Dose,
            prescription.Instructions,
            prescription.Quantity,
            prescription.Status,
            prescription.IssuedAtUtc,
            prescription.CancelledAtUtc,
            prescription.FulfillmentStatus,
            prescription.Version);
}
