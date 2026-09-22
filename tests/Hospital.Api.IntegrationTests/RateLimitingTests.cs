using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;

namespace Hospital.Api.IntegrationTests;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task BurstLimitReturnsRetryableProblemDetailsAndLogsRejection()
    {
        using RateLimitedHospitalApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/v1/system/status")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/v1/system/status")).StatusCode);

        HttpResponseMessage response = await client.GetAsync("/api/v1/system/status");
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(
            "Retry-After",
            Assert.Single(response.Headers.GetValues("Access-Control-Expose-Headers")));
        Assert.Equal(
            "http://localhost:5173",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(
            "Too many requests.",
            document.RootElement.GetProperty("title").GetString());
        Assert.True(document.RootElement.TryGetProperty("traceId", out JsonElement traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
        Assert.Contains(
            factory.LogSink.Entries,
            entry => entry.EventId.Id == 1002 &&
                entry.Message.Contains(
                    "Rate limit rejected GET api/v1/system/status",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrustedForwardedClientAddressCreatesAnIndependentPartition()
    {
        using ForwardedAddressRateLimitedHospitalApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage firstRequest = CreateForwardedRequest("203.0.113.10");
        using HttpResponseMessage firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using HttpRequestMessage repeatedRequest = CreateForwardedRequest("203.0.113.10");
        using HttpResponseMessage repeatedResponse = await client.SendAsync(repeatedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, repeatedResponse.StatusCode);

        using HttpRequestMessage independentRequest = CreateForwardedRequest("203.0.113.11");
        using HttpResponseMessage independentResponse = await client.SendAsync(independentRequest);
        Assert.Equal(HttpStatusCode.OK, independentResponse.StatusCode);
    }

    private static HttpRequestMessage CreateForwardedRequest(string clientAddress)
    {
        HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/system/status");
        request.Headers.Add("X-Forwarded-For", clientAddress);
        request.Headers.Add("X-Forwarded-Proto", "https");
        return request;
    }
}

internal sealed class RateLimitedHospitalApiFactory : HospitalApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:BurstPermitLimit", "2");
        builder.UseSetting("RateLimiting:BurstWindowSeconds", "60");
    }
}

internal sealed class ForwardedAddressRateLimitedHospitalApiFactory : HospitalApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:BurstPermitLimit", "1");
        builder.UseSetting("RateLimiting:BurstWindowSeconds", "60");
        builder.UseSetting("ReverseProxy:TrustForwardedHeaders", "true");
    }
}
