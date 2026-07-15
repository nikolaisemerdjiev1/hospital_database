namespace Hospital.Core.Profiles;

public sealed class PatientProfile
{
    public long Id { get; private set; }

    public long UserProfileId { get; init; }

    public required string MedicalRecordNumber { get; init; }

    public required DateOnly DateOfBirth { get; init; }

    public string? AllergySummary { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public UserProfile UserProfile { get; private set; } = null!;
}
