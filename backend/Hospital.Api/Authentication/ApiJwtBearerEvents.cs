using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Authentication;

internal sealed partial class ApiJwtBearerEvents(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiJwtBearerEvents> logger) : JwtBearerEvents
{
    public override Task AuthenticationFailed(AuthenticationFailedContext context)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            string failureType = context.Exception.GetType().Name;
            LogAuthenticationFailed(logger, failureType);
        }

        return Task.CompletedTask;
    }

    public override async Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";

        await WriteProblemDetailsAsync(
            context.HttpContext,
            StatusCodes.Status401Unauthorized,
            "Authentication is required.",
            "https://www.rfc-editor.org/rfc/rfc9110#name-401-unauthorized");
    }

    public override async Task Forbidden(ForbiddenContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        await WriteProblemDetailsAsync(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            "Access is forbidden.",
            "https://www.rfc-editor.org/rfc/rfc9110#name-403-forbidden");
    }

    private async Task WriteProblemDetailsAsync(
        HttpContext httpContext,
        int statusCode,
        string title,
        string type)
    {
        httpContext.Response.ContentType = "application/problem+json";

        ProblemDetails problemDetails = new()
        {
            Status = statusCode,
            Title = title,
            Type = type,
        };

        bool written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });

        if (!written)
        {
            await httpContext.Response.WriteAsJsonAsync(problemDetails);
        }
    }

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Information,
        Message = "Bearer token authentication failed with {FailureType}")]
    private static partial void LogAuthenticationFailed(ILogger logger, string failureType);
}
