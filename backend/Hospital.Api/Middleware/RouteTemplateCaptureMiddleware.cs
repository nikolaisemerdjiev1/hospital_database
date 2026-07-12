using Microsoft.AspNetCore.Routing;

namespace Hospital.Api.Middleware;

internal sealed class RouteTemplateCaptureMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        RequestRouteContext.Capture(context);

        return next(context);
    }
}

internal static class RequestRouteContext
{
    private static readonly object RouteTemplateItemKey = new();

    public static void Capture(HttpContext context)
    {
        context.Items[RouteTemplateItemKey] = ResolveRouteTemplate(context);
    }

    public static string GetRouteTemplate(HttpContext context) =>
        context.Items.TryGetValue(RouteTemplateItemKey, out object? storedRouteTemplate) &&
        storedRouteTemplate is string value
            ? value
            : ResolveRouteTemplate(context);

    private static string ResolveRouteTemplate(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? "matched-endpoint"
            : "unmatched-endpoint";
}
