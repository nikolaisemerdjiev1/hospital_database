using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Prescriptions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
[Route("api/v1/consultations/{consultationId:long}/prescriptions")]
public sealed class ConsultationPrescriptionsController(
    ILocalUserResolver localUserResolver,
    IssuePrescriptionUseCase issuePrescription) : ClinicalControllerBase
{
    [HttpPost]
    [ProducesResponseType<DoctorPrescriptionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorPrescriptionResponse>> Issue(
        long consultationId,
        IssuePrescriptionRequest request,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<DoctorPrescriptionDetails> result = await issuePrescription
            .ExecuteAsync(
                localUser.UserProfileId,
                consultationId,
                request.MedicationId,
                request.Dose,
                request.Instructions,
                request.Quantity,
                GetRequestTraceId(),
                cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(
                nameof(PrescriptionsController.Get),
                "Prescriptions",
                new { prescriptionId = result.Value!.Id },
                ToResponse(result.Value))
            : ApplicationProblem(result);
    }
}
