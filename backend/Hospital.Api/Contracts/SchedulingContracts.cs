namespace Hospital.Api.Contracts;

public sealed record ClinicianResponse(
    long Id,
    string DisplayName,
    string Specialty);

public sealed record AvailabilityResponse(
    long Id,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    uint Version);

public sealed record AppointmentResponse(
    long Id,
    long ClinicianId,
    string ClinicianDisplayName,
    string ClinicianSpecialty,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string Reason,
    string Status,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    uint Version);

public sealed record AppointmentPageResponse(
    IReadOnlyList<AppointmentResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record BookAppointmentRequest(
    long AvailabilitySlotId,
    uint ExpectedAvailabilityVersion,
    string? Reason);

public sealed record TransitionAppointmentRequest(
    string? TargetStatus,
    uint ExpectedVersion,
    string? Reason);
