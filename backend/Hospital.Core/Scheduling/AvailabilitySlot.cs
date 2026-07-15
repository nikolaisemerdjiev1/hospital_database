using Hospital.Core.Profiles;

namespace Hospital.Core.Scheduling;

public sealed class AvailabilitySlot
{
    public long Id { get; private set; }

    public long ClinicianProfileId { get; init; }

    public required DateTimeOffset StartsAtUtc { get; set; }

    public required DateTimeOffset EndsAtUtc { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public uint Version { get; private set; }

    public ClinicianProfile ClinicianProfile { get; private set; } = null!;
}
