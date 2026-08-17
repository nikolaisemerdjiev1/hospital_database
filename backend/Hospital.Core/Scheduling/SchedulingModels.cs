namespace Hospital.Core.Scheduling;

public sealed record ClinicianSummary(
    long Id,
    string DisplayName,
    string Specialty);

public sealed record AvailabilitySummary(
    long Id,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    uint Version);

public sealed record AppointmentSummary(
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

public sealed record AppointmentPage(
    IReadOnlyList<AppointmentSummary> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
