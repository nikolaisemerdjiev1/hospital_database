namespace Hospital.Api.Contracts;

public sealed record IdentityResponse(
    long UserProfileId,
    string DisplayName,
    string Role);
