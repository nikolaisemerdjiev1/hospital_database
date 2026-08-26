using Hospital.Core.Persistence;
using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Consultations;

public sealed record ConsultationReference(
    long Id,
    string Status,
    uint Version);

public sealed record DoctorWorklistItem(
    long AppointmentId,
    string PatientDisplayName,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string Reason,
    string AppointmentStatus,
    uint AppointmentVersion,
    ConsultationReference? Consultation);

public sealed record DoctorWorklistPage(
    IReadOnlyList<DoctorWorklistItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record DoctorAppointmentContext(
    long Id,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string Reason,
    string Status,
    uint Version);

public sealed record DoctorPatientContext(
    string DisplayName,
    string MedicalRecordNumber,
    DateOnly DateOfBirth,
    string? AllergySummary);

public sealed record DoctorPrescriptionSummary(
    long Id,
    string RxCui,
    string MedicationDisplayName,
    string Dose,
    string Instructions,
    int Quantity,
    string Status,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? FulfillmentStatus,
    uint Version);

public sealed record DoctorConsultationDetails(
    long Id,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    uint Version,
    DoctorAppointmentContext Appointment,
    DoctorPatientContext Patient,
    string? Outcome,
    string? ClinicalNotes,
    string? PatientSummary,
    string? CareInstructions,
    IReadOnlyList<DoctorPrescriptionSummary> Prescriptions)
{
    internal static DoctorConsultationDetails FromEntities(
        Consultation consultation,
        IReadOnlyList<DoctorPrescriptionSummary> prescriptions) =>
        new(
            consultation.Id,
            consultation.Status.ToString(),
            consultation.StartedAtUtc,
            consultation.CompletedAtUtc,
            consultation.Version,
            new DoctorAppointmentContext(
                consultation.Appointment.Id,
                consultation.Appointment.AvailabilitySlot.StartsAtUtc,
                consultation.Appointment.AvailabilitySlot.EndsAtUtc,
                consultation.Appointment.Reason,
                consultation.Appointment.Status.ToString(),
                consultation.Appointment.Version),
            new DoctorPatientContext(
                consultation.Appointment.PatientProfile.UserProfile.DisplayName,
                consultation.Appointment.PatientProfile.MedicalRecordNumber,
                consultation.Appointment.PatientProfile.DateOfBirth,
                consultation.Appointment.PatientProfile.AllergySummary),
            consultation.Outcome,
            consultation.ClinicalNotes,
            consultation.PatientSummary,
            consultation.CareInstructions,
            prescriptions);
}

internal static class DoctorPrescriptionProjection
{
    public static async Task<DoctorPrescriptionSummary[]> LoadAsync(
        IApplicationDbContext applicationDbContext,
        long consultationId,
        CancellationToken cancellationToken) =>
        await applicationDbContext.Prescriptions
                .AsNoTracking()
                .Where(prescription => prescription.ConsultationId == consultationId)
                .OrderByDescending(prescription => prescription.IssuedAtUtc)
                .ThenByDescending(prescription => prescription.Id)
                .Select(prescription => new DoctorPrescriptionSummary(
                    prescription.Id,
                    prescription.RxCuiSnapshot,
                    prescription.MedicationDisplayNameSnapshot,
                    prescription.Dose,
                    prescription.Instructions,
                    prescription.Quantity,
                    prescription.Status.ToString(),
                    prescription.IssuedAtUtc,
                    prescription.CancelledAtUtc,
                    applicationDbContext.Fulfillments
                        .Where(fulfillment => fulfillment.PrescriptionId == prescription.Id)
                        .Select(fulfillment => fulfillment.Status.ToString())
                        .SingleOrDefault(),
                    prescription.Version))
            .ToArrayAsync(cancellationToken);
}
