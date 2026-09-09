using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Prescriptions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Patient)]
[Route("api/v1/prescriptions")]
public sealed class PatientPrescriptionsController(
    ILocalUserResolver localUserResolver,
    ListPatientPrescriptionsUseCase listPrescriptions) : ApplicationControllerBase
{
    [HttpGet]
    [ProducesResponseType<PatientPrescriptionPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientPrescriptionPageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<PatientPrescriptionPage> result = await listPrescriptions.ExecuteAsync(
            localUser.UserProfileId,
            page,
            pageSize,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return ApplicationProblem(result);
        }

        PatientPrescriptionPage prescriptions = result.Value!;
        return Ok(new PatientPrescriptionPageResponse(
            prescriptions.Items.Select(static prescription => new PatientPrescriptionResponse(
                prescription.Id,
                prescription.MedicationDisplayName,
                prescription.Dose,
                prescription.Instructions,
                prescription.Quantity,
                prescription.IssuedAtUtc,
                prescription.CancelledAtUtc,
                prescription.PharmacyStatus))
                .ToArray(),
            prescriptions.Page,
            prescriptions.PageSize,
            prescriptions.TotalItems,
            prescriptions.TotalPages));
    }
}
