using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hospital.Api.IntegrationTests;

public sealed class DatabaseHospitalApiFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionStringVariable = "HOSPITAL_TEST_CONNECTION_STRING";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        string connectionString = Environment.GetEnvironmentVariable(TestConnectionStringVariable)
            ?? throw new InvalidOperationException(
                $"{TestConnectionStringVariable} must be set for PostgreSQL integration tests.");

        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Auth0:Audience", "https://hospital-coordination-api-test");
        builder.UseSetting("Authentication:Auth0:Domain", "auth.test.local");
        builder.UseSetting(
            "Authentication:Auth0:RoleClaim",
            "https://hospital.test/claims/app_role");
        builder.UseSetting("Frontend:Origin", "http://localhost:5173");
        builder.UseSetting("ConnectionStrings:HospitalDatabase", connectionString);
    }
}
