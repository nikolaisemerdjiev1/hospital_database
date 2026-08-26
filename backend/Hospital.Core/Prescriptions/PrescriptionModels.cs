namespace Hospital.Core.Prescriptions;

public sealed record DoctorPrescriptionDetails(
    long Id,
    long ConsultationId,
    long MedicationId,
    string RxCui,
    string MedicationDisplayName,
    string Dose,
    string Instructions,
    int Quantity,
    string Status,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string FulfillmentStatus,
    uint Version);
