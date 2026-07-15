using Hospital.Core.Prescriptions;
using Hospital.Core.Profiles;

namespace Hospital.Core.Pharmacy;

public sealed class Fulfillment
{
    public long Id { get; private set; }

    public long PrescriptionId { get; init; }

    public long? AssignedPharmacistProfileId { get; set; }

    public required FulfillmentStatus Status { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ReviewStartedAtUtc { get; set; }

    public DateTimeOffset? ReadyAtUtc { get; set; }

    public DateTimeOffset? DispensedAtUtc { get; set; }

    public DateTimeOffset? CancelledAtUtc { get; set; }

    public uint Version { get; private set; }

    public Prescription Prescription { get; private set; } = null!;

    public PharmacistProfile? AssignedPharmacistProfile { get; private set; }
}
