using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Prescriptions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
[Route("api/v1/prescriptions")]
public sealed class PrescriptionsController(
    ILocalUserResolver localUserResolver,
    GetDoctorPrescriptionUseCase getPrescription,
    CancelPrescriptionUseCase cancelPrescription) : ClinicalControllerBase
{
    [HttpGet("{prescriptionId:long}")]
    [ProducesResponseType<DoctorPrescriptionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoctorPrescriptionResponse>> Get(
        long prescriptionId,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<DoctorPrescriptionDetails> result = await getPrescription.ExecuteAsync(
            localUser.UserProfileId,
            prescriptionId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : ApplicationProblem(result);
    }

    [HttpPost("{prescriptionId:long}/cancellation")]
    [ProducesResponseType<DoctorPrescriptionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorPrescriptionResponse>> Cancel(
        long prescriptionId,
        CancelPrescriptionRequest request,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<DoctorPrescriptionDetails> result = await cancelPrescription
            .ExecuteAsync(
                localUser.UserProfileId,
                prescriptionId,
                request.ExpectedVersion,
                GetRequestTraceId(),
                cancellationToken);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : ApplicationProblem(result);
    }
}
