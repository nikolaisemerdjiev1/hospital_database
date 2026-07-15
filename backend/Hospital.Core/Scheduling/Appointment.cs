using Hospital.Core.Profiles;

namespace Hospital.Core.Scheduling;

public sealed class Appointment
{
    public long Id { get; private set; }

    public long PatientProfileId { get; init; }

    public long AvailabilitySlotId { get; init; }

    public required string Reason { get; set; }

    public required AppointmentStatus Status { get; set; }

    public DateTimeOffset? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public uint Version { get; private set; }

    public PatientProfile PatientProfile { get; private set; } = null!;

    public AvailabilitySlot AvailabilitySlot { get; private set; } = null!;
}
