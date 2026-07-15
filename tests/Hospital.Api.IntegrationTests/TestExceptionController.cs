using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.IntegrationTests;

[ApiController]
[AllowAnonymous]
[Route("__test")]
public sealed class TestExceptionController : ControllerBase
{
    [HttpGet("exception")]
    public IActionResult GetException()
    {
        _ = HttpContext.TraceIdentifier;
        throw new InvalidOperationException("Test exception details must never reach the client.");
    }
}
