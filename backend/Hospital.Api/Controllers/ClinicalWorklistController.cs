using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Consultations;
using Hospital.Core.Scheduling;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
[Route("api/v1/clinical-worklist")]
public sealed class ClinicalWorklistController(
    ILocalUserResolver localUserResolver,
    ListDoctorWorklistUseCase listDoctorWorklist) : ApplicationControllerBase
{
    [HttpGet]
    [ProducesResponseType<DoctorWorklistPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoctorWorklistPageResponse>> List(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!from.HasValue || !to.HasValue)
        {
            return Problem(
                detail: "Both from and to timestamps are required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The worklist window is required.",
                type: "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "worklist_window_required",
                });
        }

        if (!TryParseStatus(status, out AppointmentStatus? parsedStatus))
        {
            return Problem(
                detail: "Status must be Scheduled, InProgress, Completed, Cancelled, or NoShow.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The appointment status is invalid.",
                type: "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "invalid_appointment_status",
                });
        }

        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<DoctorWorklistPage> result = await listDoctorWorklist.ExecuteAsync(
            localUser.UserProfileId,
            from.Value,
            to.Value,
            page,
            pageSize,
            parsedStatus,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return ApplicationProblem(result);
        }

        DoctorWorklistPage worklist = result.Value!;
        return Ok(new DoctorWorklistPageResponse(
            worklist.Items.Select(static item => new DoctorWorklistItemResponse(
                item.AppointmentId,
                item.PatientDisplayName,
                item.StartsAtUtc,
                item.EndsAtUtc,
                item.Reason,
                item.AppointmentStatus,
                item.AppointmentVersion,
                item.Consultation is null
                    ? null
                    : new ConsultationReferenceResponse(
                        item.Consultation.Id,
                        item.Consultation.Status,
                        item.Consultation.Version)))
                .ToArray(),
            worklist.Page,
            worklist.PageSize,
            worklist.TotalItems,
            worklist.TotalPages));
    }

    private static bool TryParseStatus(
        string? value,
        out AppointmentStatus? appointmentStatus)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            appointmentStatus = null;
            return true;
        }

        bool parsed = Enum.TryParse(value, ignoreCase: true, out AppointmentStatus status) &&
            Enum.IsDefined(status);
        appointmentStatus = parsed ? status : null;
        return parsed;
    }
}
