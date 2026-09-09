namespace Hospital.Api.Contracts;

public sealed record PharmacyWorkQueueItemResponse(
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

public sealed record PharmacyWorkQueuePageResponse(
    IReadOnlyList<PharmacyWorkQueueItemResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record PharmacyPatientContextResponse(
    string DisplayName,
    string? AllergySummary);

public sealed record PharmacyPrescriptionContextResponse(
    long Id,
    string RxCui,
    string MedicationDisplayName,
    string Dose,
    string Instructions,
    int Quantity,
    string Status,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string PrescriberDisplayName);

public sealed record PharmacyFulfillmentResponse(
    long Id,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewStartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? DispensedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    uint Version,
    bool AssignedToCurrentPharmacist,
    PharmacyPatientContextResponse Patient,
    PharmacyPrescriptionContextResponse Prescription);

public sealed record TransitionFulfillmentRequest(
    string? TargetStatus,
    uint ExpectedVersion);
