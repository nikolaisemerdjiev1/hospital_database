using Hospital.Api.Contracts;
using Hospital.Core.Scheduling;

using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

public abstract class SchedulingControllerBase : ControllerBase
{
    protected ObjectResult SchedulingProblem<T>(SchedulingResult<T> result)
    {
        int statusCode = result.Failure switch
        {
            SchedulingFailure.Validation => StatusCodes.Status400BadRequest,
            SchedulingFailure.NotFound => StatusCodes.Status404NotFound,
            SchedulingFailure.Conflict => StatusCodes.Status409Conflict,
            _ => throw new InvalidOperationException(
                "A successful scheduling result cannot be mapped to a problem response."),
        };

        string title = result.Failure switch
        {
            SchedulingFailure.Validation => "The scheduling request is invalid.",
            SchedulingFailure.NotFound => "The requested scheduling resource was not found.",
            SchedulingFailure.Conflict => "The scheduling request conflicts with current state.",
            _ => throw new InvalidOperationException(
                "A successful scheduling result cannot be mapped to a problem response."),
        };

        return Problem(
            detail: result.ErrorMessage,
            statusCode: statusCode,
            title: title,
            type: GetProblemType(statusCode),
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = result.ErrorCode,
            });
    }

    protected static AppointmentResponse ToResponse(AppointmentSummary appointment) =>
        new(
            appointment.Id,
            appointment.ClinicianId,
            appointment.ClinicianDisplayName,
            appointment.ClinicianSpecialty,
            appointment.StartsAtUtc,
            appointment.EndsAtUtc,
            appointment.Reason,
            appointment.Status,
            appointment.CancelledAtUtc,
            appointment.CancellationReason,
            appointment.Version);

    private static string GetProblemType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest =>
            "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request",
        StatusCodes.Status404NotFound =>
            "https://www.rfc-editor.org/rfc/rfc9110#name-404-not-found",
        StatusCodes.Status409Conflict =>
            "https://www.rfc-editor.org/rfc/rfc9110#name-409-conflict",
        _ => throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode, null),
    };
}
