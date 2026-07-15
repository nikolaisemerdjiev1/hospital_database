using Hospital.Core.Profiles;

namespace Hospital.Core.Audit;

public sealed class AuditEvent
{
    public long Id { get; private set; }

    public long? ActorUserProfileId { get; init; }

    public required string Action { get; init; }

    public required string AffectedEntityType { get; init; }

    public long? AffectedEntityId { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public string? TraceId { get; init; }

    public string? MetadataJson { get; init; }

    public UserProfile? ActorUserProfile { get; private set; }
}
