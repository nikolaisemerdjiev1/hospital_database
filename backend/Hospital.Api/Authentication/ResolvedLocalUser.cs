using Hospital.Core.Profiles;

namespace Hospital.Api.Authentication;

public sealed record ResolvedLocalUser(
    long UserProfileId,
    string DisplayName,
    ProfileType ProfileType);
