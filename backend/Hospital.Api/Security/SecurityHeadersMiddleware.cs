namespace Hospital.Api.Security;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public static void ApplySpaPolicy(HttpContext context, string authDomain)
    {
        // Domain is validated by Auth0Options. Tokens remain in memory; no wildcard origins.
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; base-uri 'none'; object-src 'none'; " +
            "script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; " +
            "img-src 'self' data:; font-src 'self' data:; worker-src 'self' blob:; " +
            $"connect-src 'self' https://{authDomain}; frame-src https://{authDomain}; " +
            "form-action 'none'; frame-ancestors 'none'";
    }

    public Task InvokeAsync(HttpContext context)
    {
        IHeaderDictionary headers = context.Response.Headers;
        headers.ContentSecurityPolicy =
            "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers.Append(
            "Permissions-Policy",
            "camera=(), geolocation=(), microphone=(), payment=(), usb=()");

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }

        return next(context);
    }
}
