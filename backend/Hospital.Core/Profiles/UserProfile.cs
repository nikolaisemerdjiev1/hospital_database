namespace Hospital.Core.Profiles;

public sealed class UserProfile
{
    public long Id { get; private set; }

    public required string Auth0Subject { get; init; }

    public required string DisplayName { get; set; }

    public required ProfileType ProfileType { get; init; }

    public required AccountStatus Status { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
