using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Consultations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
[Route("api/v1/consultations")]
public sealed class ConsultationsController(
    ILocalUserResolver localUserResolver,
    GetDoctorConsultationUseCase getConsultation,
    SaveConsultationDraftUseCase saveConsultation,
    CompleteConsultationUseCase completeConsultation) : ClinicalControllerBase
{
    [HttpGet("{consultationId:long}")]
    [ProducesResponseType<DoctorConsultationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoctorConsultationResponse>> Get(
        long consultationId,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<DoctorConsultationDetails> result = await getConsultation.ExecuteAsync(
            localUser.UserProfileId,
            consultationId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : ApplicationProblem(result);
    }

    [HttpPut("{consultationId:long}")]
    [ProducesResponseType<DoctorConsultationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorConsultationResponse>> SaveDraft(
        long consultationId,
        SaveConsultationDraftRequest request,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<DoctorConsultationDetails> result = await saveConsultation.ExecuteAsync(
            localUser.UserProfileId,
            consultationId,
            request.Outcome,
            request.ClinicalNotes,
            request.PatientSummary,
            request.CareInstructions,
            request.ExpectedVersion,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : ApplicationProblem(result);
    }

    [HttpPost("{consultationId:long}/completion")]
    [ProducesResponseType<DoctorConsultationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorConsultationResponse>> Complete(
        long consultationId,
        CompleteConsultationRequest request,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<DoctorConsultationDetails> result = await completeConsultation
            .ExecuteAsync(
                localUser.UserProfileId,
                consultationId,
                request.Outcome,
                request.ClinicalNotes,
                request.PatientSummary,
                request.CareInstructions,
                request.ExpectedVersion,
                GetRequestTraceId(),
                cancellationToken);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : ApplicationProblem(result);
    }
}
