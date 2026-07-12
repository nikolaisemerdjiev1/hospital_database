using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Hospital.Api.Contracts;

namespace Hospital.Api.IntegrationTests;

public sealed class SystemEndpointsTests(HospitalApiFactory factory)
    : IClassFixture<HospitalApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task StatusReturnsServiceReadiness()
    {
        SystemStatusResponse? response = await client.GetFromJsonAsync<SystemStatusResponse>(
            "/api/v1/system/status");

        Assert.NotNull(response);
        Assert.Equal("Hospital Coordination API", response.Service);
        Assert.Equal("ready", response.Status);
        Assert.NotEmpty(response.Environment);
    }

    [Fact]
    public async Task LivenessEndpointIsHealthy()
    {
        HttpResponseMessage response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApiDocumentIsAvailable()
    {
        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownRouteReturnsProblemDetailsWithTraceId()
    {
        HttpResponseMessage response = await client.GetAsync("/not-a-real-route");
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(document.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task UnhandledExceptionReturnsSafeProblemDetailsAndLogsStatus500()
    {
        factory.LogSink.Clear();

        HttpResponseMessage response = await client.GetAsync("/__test/exception");
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "An unexpected error occurred.",
            document.RootElement.GetProperty("title").GetString());
        Assert.True(document.RootElement.TryGetProperty("traceId", out JsonElement traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
        Assert.DoesNotContain("Test exception details", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            factory.LogSink.Entries,
            entry =>
                entry.EventId.Id == 1001 &&
                entry.Message.Contains("__test/exception", StringComparison.Ordinal) &&
                entry.Message.Contains("responded 500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllowedOriginPreflightReturnsExactCorsOrigin()
    {
        using HttpRequestMessage request = new(HttpMethod.Options, "/api/v1/system/status");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "http://localhost:5173",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains(
            "GET",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnapprovedOriginDoesNotReceiveCorsPermission()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/system/status");
        request.Headers.Add("Origin", "https://unapproved.example");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
