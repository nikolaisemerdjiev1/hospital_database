using Hospital.Api.Authentication;
using Hospital.Api.Contracts;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ProfileResolved)]
[Route("api/v1/identity")]
public sealed class IdentityController(
    ILocalUserResolver localUserResolver) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<IdentityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IdentityResponse>> GetCurrentUser(
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);

        if (localUser is null)
        {
            return Forbid();
        }

        return Ok(new IdentityResponse(
            localUser.UserProfileId,
            localUser.DisplayName,
            ApplicationRoles.GetRole(localUser.ProfileType)));
    }
}
