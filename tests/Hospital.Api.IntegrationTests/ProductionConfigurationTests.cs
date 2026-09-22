using System.Net;

using Microsoft.AspNetCore.Hosting;

namespace Hospital.Api.IntegrationTests;

public sealed class ProductionConfigurationTests
{
    private const string ValidRevision =
        "a36a65ca8f385261bc53e276309da508981ea997";

    [Fact]
    public void ProductionRejectsANonCommitReleaseRevision()
    {
        using ProductionHospitalApiFactory factory = new(
            revision: "local",
            trustForwardedHeaders: true);

        Exception exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        Assert.Contains(
            "Release:Revision must be a lowercase 40-character Git commit SHA in Production",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRejectsDisabledForwardedHeaderTrust()
    {
        using ProductionHospitalApiFactory factory = new(
            revision: ValidRevision,
            trustForwardedHeaders: false);

        Exception exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        Assert.Contains(
            "ReverseProxy:TrustForwardedHeaders must be true in Production",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionRecognizesForwardedHttpsAndAddsHsts()
    {
        using ProductionHospitalApiFactory factory = new(
            revision: ValidRevision,
            trustForwardedHeaders: true);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/system/status");
        request.Headers.Host = "api.example.test";
        request.Headers.Add("X-Forwarded-For", "203.0.113.10");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "max-age=",
            Assert.Single(response.Headers.GetValues("Strict-Transport-Security")),
            StringComparison.Ordinal);
    }
}

internal sealed class ProductionHospitalApiFactory(
    string revision,
    bool trustForwardedHeaders) : HospitalApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Production");
        builder.UseSetting("Release:Revision", revision);
        builder.UseSetting(
            "ReverseProxy:TrustForwardedHeaders",
            trustForwardedHeaders.ToString());
    }
}
