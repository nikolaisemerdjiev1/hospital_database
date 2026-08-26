namespace Hospital.Api.Contracts;

public sealed record ConsultationReferenceResponse(
    long Id,
    string Status,
    uint Version);

public sealed record DoctorWorklistItemResponse(
    long AppointmentId,
    string PatientDisplayName,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string Reason,
    string AppointmentStatus,
    uint AppointmentVersion,
    ConsultationReferenceResponse? Consultation);

public sealed record DoctorWorklistPageResponse(
    IReadOnlyList<DoctorWorklistItemResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record StartConsultationRequest(uint ExpectedAppointmentVersion);

public sealed record SaveConsultationDraftRequest(
    string? Outcome,
    string? ClinicalNotes,
    string? PatientSummary,
    string? CareInstructions,
    uint ExpectedVersion);

public sealed record CompleteConsultationRequest(
    string? Outcome,
    string? ClinicalNotes,
    string? PatientSummary,
    string? CareInstructions,
    uint ExpectedVersion);

public sealed record DoctorAppointmentContextResponse(
    long Id,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string Reason,
    string Status,
    uint Version);

public sealed record DoctorPatientContextResponse(
    string DisplayName,
    string MedicalRecordNumber,
    DateOnly DateOfBirth,
    string? AllergySummary);

public sealed record DoctorPrescriptionSummaryResponse(
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

public sealed record DoctorConsultationResponse(
    long Id,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    uint Version,
    DoctorAppointmentContextResponse Appointment,
    DoctorPatientContextResponse Patient,
    string? Outcome,
    string? ClinicalNotes,
    string? PatientSummary,
    string? CareInstructions,
    IReadOnlyList<DoctorPrescriptionSummaryResponse> Prescriptions);

public sealed record MedicationCatalogItemResponse(
    long MedicationId,
    string RxCui,
    string DisplayName,
    string? ConceptType,
    string? Strength,
    string? DoseForm,
    string Source);

public sealed record MedicationCatalogSearchResponse(
    IReadOnlyList<MedicationCatalogItemResponse> Items,
    string CatalogStatus);

public sealed record IssuePrescriptionRequest(
    long MedicationId,
    string? Dose,
    string? Instructions,
    int Quantity);

public sealed record CancelPrescriptionRequest(uint ExpectedVersion);

public sealed record DoctorPrescriptionResponse(
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
