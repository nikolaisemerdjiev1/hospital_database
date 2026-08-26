using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Scheduling;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Patient)]
[Route("api/v1/clinicians")]
public sealed class CliniciansController(
    ListCliniciansUseCase listClinicians,
    ListClinicianAvailabilityUseCase listAvailability) : SchedulingControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClinicianResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ClinicianResponse>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClinicianSummary> clinicians = await listClinicians.ExecuteAsync(
            cancellationToken);

        return Ok(clinicians.Select(static clinician => new ClinicianResponse(
            clinician.Id,
            clinician.DisplayName,
            clinician.Specialty)));
    }

    [HttpGet("{clinicianProfileId:long}/availability")]
    [ProducesResponseType<IReadOnlyList<AvailabilityResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AvailabilityResponse>>> ListAvailability(
        long clinicianProfileId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue)
        {
            return Problem(
                detail: "Both from and to UTC timestamps are required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The availability window is required.",
                type: "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request");
        }

        ApplicationResult<IReadOnlyList<AvailabilitySummary>> result =
            await listAvailability.ExecuteAsync(
                clinicianProfileId,
                from.Value,
                to.Value,
                cancellationToken);

        if (!result.IsSuccess)
        {
            return ApplicationProblem(result);
        }

        return Ok(result.Value!.Select(static slot => new AvailabilityResponse(
            slot.Id,
            slot.StartsAtUtc,
            slot.EndsAtUtc,
            slot.Version)));
    }
}
