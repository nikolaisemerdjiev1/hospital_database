using Hospital.Core.Profiles;

using Microsoft.AspNetCore.Authorization;

namespace Hospital.Api.Authentication;

internal sealed class LocalProfileAuthorizationHandler(
    ILocalUserResolver localUserResolver) : AuthorizationHandler<LocalProfileRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        LocalProfileRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        CancellationToken cancellationToken = context.Resource is HttpContext httpContext
            ? httpContext.RequestAborted
            : CancellationToken.None;

        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            context.User,
            cancellationToken);

        ProfileType? requiredProfileType = requirement.RequiredProfileType;
        if (localUser is not null &&
            (requiredProfileType is null || localUser.ProfileType == requiredProfileType))
        {
            context.Succeed(requirement);
        }
    }
}
