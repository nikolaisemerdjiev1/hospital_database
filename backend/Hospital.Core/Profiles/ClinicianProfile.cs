namespace Hospital.Core.Profiles;

public sealed class ClinicianProfile
{
    public long Id { get; private set; }

    public long UserProfileId { get; init; }

    public required string StaffIdentifier { get; init; }

    public required string Specialty { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public UserProfile UserProfile { get; private set; } = null!;
}
