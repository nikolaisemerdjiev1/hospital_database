using System.Diagnostics;

using Hospital.Api.Middleware;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.ErrorHandling;

internal sealed partial class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string routeTemplate = RequestRouteContext.GetRouteTemplate(httpContext);
        string traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        LogUnhandledException(
            logger,
            httpContext.Request.Method,
            routeTemplate,
            traceId,
            exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://www.rfc-editor.org/rfc/rfc9110#name-500-internal-server-error",
            },
        });
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled exception while processing {RequestMethod} {RouteTemplate} with trace {TraceId}")]
    private static partial void LogUnhandledException(
        ILogger logger,
        string requestMethod,
        string routeTemplate,
        string traceId,
        Exception exception);
}
