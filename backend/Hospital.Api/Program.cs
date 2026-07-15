using System.Diagnostics;

using Hospital.Api.Authentication;
using Hospital.Api.Configuration;
using Hospital.Api.ErrorHandling;
using Hospital.Api.Middleware;
using Hospital.Infrastructure;
using Hospital.Infrastructure.Persistence.Initialization;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
bool initializeDatabase = args.Contains("--initialize-database", StringComparer.Ordinal);

builder.Logging.ClearProviders();

if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}
else
{
    builder.Logging.AddJsonConsole();
}

builder.Services
    .AddOptions<FrontendOptions>()
    .BindConfiguration(FrontendOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        FrontendOptions.HasCanonicalHttpOrigin,
        "Frontend:Origin must be an absolute HTTP(S) origin without credentials, a path, query, fragment, or trailing slash.")
    .ValidateOnStart();

string frontendOrigin = builder.Configuration
    .GetRequiredSection(FrontendOptions.SectionName)
    .GetValue<string>(nameof(FrontendOptions.Origin))
    ?? throw new InvalidOperationException("Frontend:Origin configuration is required.");

string databaseConnectionString = builder.Configuration
    .GetConnectionString("HospitalDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:HospitalDatabase configuration is required.");

builder.Services.AddInfrastructure(databaseConnectionString);
builder.Services.AddApplicationAuthentication(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(frontendOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        string traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

if (initializeDatabase)
{
    DemoSeedOptions seedOptions = builder.Configuration
        .GetRequiredSection(DemoSeedOptions.SectionName)
        .Get<DemoSeedOptions>()
        ?? throw new InvalidOperationException(
            $"{DemoSeedOptions.SectionName} configuration is required for database initialization.");

    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    DatabaseInitializer initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync(seedOptions);
    return;
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseMiddleware<RouteTemplateCaptureMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static registration =>
        registration.Tags.Contains(
            InfrastructureServiceCollectionExtensions.DatabaseReadinessTag),
}).AllowAnonymous();
app.MapControllers();
app.MapFallback(static () => Results.NotFound()).AllowAnonymous();

app.Run();

public partial class Program;
