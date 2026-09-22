using Hospital.Api.Configuration;
using Hospital.Api.Contracts;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hospital.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/system")]
public sealed class SystemController(
    IHostEnvironment environment,
    IOptions<ReleaseOptions> releaseOptions,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<SystemStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemStatusResponse> GetStatus()
    {
        SystemStatusResponse response = new(
            Service: "Hospital Coordination API",
            Status: "online",
            Environment: environment.EnvironmentName,
            Revision: releaseOptions.Value.Revision,
            Timestamp: timeProvider.GetUtcNow());

        return Ok(response);
    }
}
