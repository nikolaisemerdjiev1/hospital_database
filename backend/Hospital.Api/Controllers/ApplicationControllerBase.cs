using System.Diagnostics;

using Hospital.Core.Application;

using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

public abstract class ApplicationControllerBase : ControllerBase
{
    protected string GetRequestTraceId() =>
        Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    protected ObjectResult ApplicationProblem<T>(ApplicationResult<T> result)
    {
        int statusCode = result.Failure switch
        {
            ApplicationFailure.Validation => StatusCodes.Status400BadRequest,
            ApplicationFailure.NotFound => StatusCodes.Status404NotFound,
            ApplicationFailure.Conflict => StatusCodes.Status409Conflict,
            ApplicationFailure.DependencyUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => throw new InvalidOperationException(
                "A successful application result cannot be mapped to a problem response."),
        };

        string title = result.Failure switch
        {
            ApplicationFailure.Validation => "The request is invalid.",
            ApplicationFailure.NotFound => "The requested resource was not found.",
            ApplicationFailure.Conflict => "The request conflicts with current state.",
            ApplicationFailure.DependencyUnavailable => "A required service is temporarily unavailable.",
            _ => throw new InvalidOperationException(
                "A successful application result cannot be mapped to a problem response."),
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

    private static string GetProblemType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest =>
            "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request",
        StatusCodes.Status404NotFound =>
            "https://www.rfc-editor.org/rfc/rfc9110#name-404-not-found",
        StatusCodes.Status409Conflict =>
            "https://www.rfc-editor.org/rfc/rfc9110#name-409-conflict",
        StatusCodes.Status503ServiceUnavailable =>
            "https://www.rfc-editor.org/rfc/rfc9110#name-503-service-unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode, null),
    };
}
