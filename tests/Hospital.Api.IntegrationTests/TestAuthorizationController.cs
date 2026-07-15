using Hospital.Api.Authentication;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.IntegrationTests;

[ApiController]
[Route("__test/authorization")]
public sealed class TestAuthorizationController : ControllerBase
{
    [HttpGet("fallback")]
    public IActionResult Fallback() => Ok();

    [Authorize(Policy = AuthorizationPolicies.Patient)]
    [HttpGet("patient")]
    public IActionResult Patient() => Ok();

    [Authorize(Policy = AuthorizationPolicies.Doctor)]
    [HttpGet("doctor")]
    public IActionResult Doctor() => Ok();

    [Authorize(Policy = AuthorizationPolicies.Pharmacist)]
    [HttpGet("pharmacist")]
    public IActionResult Pharmacist() => Ok();

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [HttpGet("administrator")]
    public IActionResult Administrator() => Ok();
}
