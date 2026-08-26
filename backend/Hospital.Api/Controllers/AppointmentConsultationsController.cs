using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Consultations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
[Route("api/v1/appointments/{appointmentId:long}/consultations")]
public sealed class AppointmentConsultationsController(
    ILocalUserResolver localUserResolver,
    StartConsultationUseCase startConsultation) : ClinicalControllerBase
{
    [HttpPost]
    [ProducesResponseType<DoctorConsultationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorConsultationResponse>> Start(
        long appointmentId,
        StartConsultationRequest request,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<DoctorConsultationDetails> result = await startConsultation.ExecuteAsync(
            localUser.UserProfileId,
            appointmentId,
            request.ExpectedAppointmentVersion,
            GetRequestTraceId(),
            cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(
                nameof(ConsultationsController.Get),
                "Consultations",
                new { consultationId = result.Value!.Id },
                ToResponse(result.Value))
            : ApplicationProblem(result);
    }
}
