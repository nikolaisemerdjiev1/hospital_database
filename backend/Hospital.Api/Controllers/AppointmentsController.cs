using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Scheduling;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Patient)]
[Route("api/v1/appointments")]
public sealed class AppointmentsController(
    ILocalUserResolver localUserResolver,
    ListPatientAppointmentsUseCase listAppointments,
    BookAppointmentUseCase bookAppointment,
    CancelPatientAppointmentUseCase cancelAppointment) : SchedulingControllerBase
{
    [HttpGet]
    [ProducesResponseType<AppointmentPageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AppointmentPageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseStatus(status, out AppointmentStatus? parsedStatus))
        {
            return Problem(
                detail: "Status must be Scheduled, InProgress, Completed, Cancelled, or NoShow.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The appointment status is invalid.",
                type: "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request");
        }

        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<AppointmentPage> result = await listAppointments.ExecuteAsync(
            localUser.UserProfileId,
            page,
            pageSize,
            parsedStatus,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return ApplicationProblem(result);
        }

        AppointmentPage appointments = result.Value!;
        return Ok(new AppointmentPageResponse(
            appointments.Items.Select(ToResponse).ToArray(),
            appointments.Page,
            appointments.PageSize,
            appointments.TotalItems,
            appointments.TotalPages));
    }

    [HttpPost]
    [ProducesResponseType<AppointmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentResponse>> Book(
        BookAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<AppointmentSummary> result = await bookAppointment.ExecuteAsync(
            localUser.UserProfileId,
            request.AvailabilitySlotId,
            request.ExpectedAvailabilityVersion,
            request.Reason,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return ApplicationProblem(result);
        }

        return StatusCode(StatusCodes.Status201Created, ToResponse(result.Value!));
    }

    [HttpPost("{appointmentId:long}/transitions")]
    [ProducesResponseType<AppointmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentResponse>> Transition(
        long appointmentId,
        TransitionAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<AppointmentSummary> result = await cancelAppointment.ExecuteAsync(
            localUser.UserProfileId,
            appointmentId,
            request.ExpectedVersion,
            request.TargetStatus,
            request.Reason,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : ApplicationProblem(result);
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
