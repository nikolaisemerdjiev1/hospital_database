using Hospital.Core.Profiles;

using Microsoft.AspNetCore.Authorization;

namespace Hospital.Api.Authentication;

internal sealed record LocalProfileRequirement(
    ProfileType? RequiredProfileType = null) : IAuthorizationRequirement;
