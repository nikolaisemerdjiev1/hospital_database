namespace Hospital.Api.Contracts;

public sealed record PatientPrescriptionResponse(
    long Id,
    string MedicationDisplayName,
    string Dose,
    string Instructions,
    int Quantity,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string PharmacyStatus);

public sealed record PatientPrescriptionPageResponse(
    IReadOnlyList<PatientPrescriptionResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
