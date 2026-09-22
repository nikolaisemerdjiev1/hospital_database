using Hospital.Api.Authentication;
using Hospital.Api.Security;

using Microsoft.Extensions.Options;

namespace Hospital.Api.Hosting;

internal static class SpaHostingExtensions
{
    public static void UseSpaAssets(this WebApplication app)
    {
        // Only the build output is public. Never expose the content root or configuration.
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/assets") ||
                context.Request.Path == "/hospital-mark.svg",
            assets => assets.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers.CacheControl =
                        context.Context.Request.Path.StartsWithSegments("/assets")
                            ? "public,max-age=31536000,immutable"
                            : "no-cache";
                },
            }));
    }

    public static void MapSpa(this WebApplication app)
    {
        string indexPath = Path.Combine(
            app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
            "index.html");

        IResult Shell(HttpContext context, IOptions<Auth0Options> auth)
        {
            if (Path.HasExtension(context.Request.Path.Value) || !File.Exists(indexPath))
            {
                return Results.NotFound();
            }

            SecurityHeadersMiddleware.ApplySpaPolicy(context, auth.Value.Domain);
            context.Response.Headers.CacheControl = "no-cache";
            return Results.File(indexPath, "text/html; charset=utf-8");
        }

        foreach (string route in new[] { "/", "/app", "/app/{**path}", "/auth/callback" })
        {
            app.MapMethods(route, [HttpMethods.Get, HttpMethods.Head], Shell)
                .AllowAnonymous()
                .DisableRateLimiting();
        }
    }
}
