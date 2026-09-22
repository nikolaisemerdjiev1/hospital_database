using System.Net;

using Microsoft.AspNetCore.Hosting;

namespace Hospital.Api.IntegrationTests;

public sealed class SpaHostingTests : IDisposable
{
    private readonly SpaFactory factory = new();

    [Theory]
    [InlineData("/")]
    [InlineData("/app/patient")]
    [InlineData("/app/doctor/consultations/12")]
    [InlineData("/app/pharmacy")]
    [InlineData("/auth/callback?code=synthetic-callback-code&state=synthetic-state")]
    public async Task PublicShellWorksWithoutDatabaseAndHasFrontendPolicy(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"root\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' https://auth.test.local", csp, StringComparison.Ordinal);
        Assert.Contains("worker-src 'self' blob:", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.LogSink.Entries,
            entry => entry.Message.Contains("synthetic-callback-code", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/missing")]
    [InlineData("/health/missing")]
    [InlineData("/assets/missing.js")]
    [InlineData("/app/missing.js")]
    [InlineData("/.env")]
    [InlineData("/appsettings.json")]
    [InlineData("/unknown")]
    public async Task UnknownOrPrivateFilesNeverReturnSpa(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("id=\"root\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HeadHasNoBodyAndPostDoesNotFallThroughToHtml()
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/app/doctor"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        using HttpResponseMessage post = await client.PostAsync("/app/doctor", null);
        Assert.False(post.IsSuccessStatusCode);
        Assert.NotEqual("text/html", post.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AssetsAreCacheableWithoutExhaustingApiBurstAndApiStillRejectsAnonymous()
    {
        using HttpClient client = factory.CreateClient();
        for (int i = 0; i < 25; i++)
        {
            using HttpResponseMessage asset = await client.GetAsync("/assets/index-abcdefgh.js");
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.Contains("immutable", asset.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        }

        using HttpResponseMessage identity = await client.GetAsync("/api/v1/identity/me");
        Assert.Equal(HttpStatusCode.Unauthorized, identity.StatusCode);
        Assert.Equal("no-store", identity.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("script-src 'self'",
            Assert.Single(identity.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        using HttpResponseMessage ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        using HttpResponseMessage live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    public void Dispose() => factory.Dispose();

    private sealed class SpaFactory : HospitalApiFactory
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"hospital-spa-test-{Guid.NewGuid():N}");

        public SpaFactory()
        {
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><body><div id=\"root\"></div></body></html>");
            File.WriteAllText(Path.Combine(root, "assets", "index-abcdefgh.js"), "export const synthetic = true;");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseWebRoot(root);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
