namespace Hospital.Core.Profiles;

public sealed class PharmacistProfile
{
    public long Id { get; private set; }

    public long UserProfileId { get; init; }

    public required string StaffIdentifier { get; init; }

    public required string PharmacyName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public UserProfile UserProfile { get; private set; } = null!;
}
