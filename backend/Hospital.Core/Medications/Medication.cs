namespace Hospital.Core.Medications;

public sealed class Medication
{
    public long Id { get; private set; }

    public required string RxCui { get; init; }

    public required string DisplayName { get; set; }

    public string? Strength { get; set; }

    public string? DoseForm { get; set; }

    public string? Classification { get; set; }

    public required MedicationSource Source { get; set; }

    public DateTimeOffset? LastVerifiedAtUtc { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
