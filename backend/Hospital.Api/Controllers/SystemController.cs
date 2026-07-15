using Hospital.Api.Contracts;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/system")]
public sealed class SystemController(IHostEnvironment environment) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<SystemStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemStatusResponse> GetStatus()
    {
        SystemStatusResponse response = new(
            Service: "Hospital Coordination API",
            Status: "online",
            Environment: environment.EnvironmentName,
            Timestamp: DateTimeOffset.UtcNow);

        return Ok(response);
    }
}
