using System.Net;

namespace Hospital.Api.IntegrationTests;

public sealed class DatabaseReadinessTests(DatabaseHospitalApiFactory factory)
    : IClassFixture<DatabaseHospitalApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task ReadinessEndpointIsHealthyWhenPostgresIsAvailable()
    {
        HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
