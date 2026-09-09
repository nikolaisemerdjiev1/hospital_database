namespace Hospital.Core.Pharmacy;

public sealed record PharmacyWorkQueueItem(
    long FulfillmentId,
    long PrescriptionId,
    string PatientDisplayName,
    string MedicationDisplayName,
    string Dose,
    int Quantity,
    DateTimeOffset IssuedAtUtc,
    string FulfillmentStatus,
    DateTimeOffset CreatedAtUtc,
    uint Version);

public sealed record PharmacyWorkQueuePage(
    IReadOnlyList<PharmacyWorkQueueItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record PharmacyFulfillmentDetails(
    long Id,
    long PrescriptionId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewStartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? DispensedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    uint Version,
    string PatientDisplayName,
    string? AllergySummary,
    string PrescriberDisplayName,
    string RxCui,
    string MedicationDisplayName,
    string Dose,
    string Instructions,
    int Quantity,
    string PrescriptionStatus,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? PrescriptionCancelledAtUtc,
    bool AssignedToCurrentPharmacist);
