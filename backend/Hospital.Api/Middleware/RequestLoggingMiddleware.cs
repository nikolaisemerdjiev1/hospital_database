using System.Diagnostics;

namespace Hospital.Api.Middleware;

internal sealed partial class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            double elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            string routeTemplate = RequestRouteContext.GetRouteTemplate(context);
            string traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            LogRequestCompleted(
                logger,
                context.Request.Method,
                routeTemplate,
                context.Response.StatusCode,
                elapsedMilliseconds,
                traceId);
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "HTTP {RequestMethod} {RouteTemplate} responded {StatusCode} in {ElapsedMilliseconds:F1} ms with trace {TraceId}")]
    private static partial void LogRequestCompleted(
        ILogger logger,
        string requestMethod,
        string routeTemplate,
        int statusCode,
        double elapsedMilliseconds,
        string traceId);
}
