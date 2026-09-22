using System.Diagnostics;

using Hospital.Api.Authentication;
using Hospital.Api.Configuration;
using Hospital.Api.ErrorHandling;
using Hospital.Api.Hosting;
using Hospital.Api.Middleware;
using Hospital.Api.RateLimiting;
using Hospital.Api.Security;
using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Pharmacy;
using Hospital.Core.Prescriptions;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure;
using Hospital.Infrastructure.Medications;
using Hospital.Infrastructure.Persistence.Initialization;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
bool initializeDatabase = args.Contains("--initialize-database", StringComparer.Ordinal);
bool resetDemoData = args.Contains("--reset-demo-data", StringComparer.Ordinal);
bool prepareRelease = args.Contains("--prepare-release", StringComparer.Ordinal);

if (initializeDatabase && resetDemoData)
{
    throw new InvalidOperationException(
        "Choose either --initialize-database or --reset-demo-data, not both.");
}

if (prepareRelease && (initializeDatabase || resetDemoData))
{
    throw new InvalidOperationException("--prepare-release cannot be combined with another maintenance mode.");
}

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

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

builder.Services
    .AddOptions<ReleaseOptions>()
    .BindConfiguration(ReleaseOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        ReleaseOptions.IsLocalOrGitRevision,
        "Release:Revision must be 'local' or a lowercase 40-character Git commit SHA.")
    .Validate(
        options => !builder.Environment.IsProduction() ||
            ReleaseOptions.IsDeployableGitRevision(options),
        "Release:Revision must be a lowercase 40-character Git commit SHA in Production.")
    .ValidateOnStart();

IConfigurationSection reverseProxySection = builder.Configuration.GetRequiredSection(
    ReverseProxyOptions.SectionName);
ReverseProxyOptions reverseProxyConfiguration = reverseProxySection.Get<ReverseProxyOptions>()
    ?? throw new InvalidOperationException(
        $"{ReverseProxyOptions.SectionName} configuration is required.");
builder.Services
    .AddOptions<ReverseProxyOptions>()
    .Bind(reverseProxySection)
    .Validate(
        options => !builder.Environment.IsProduction() || options.TrustForwardedHeaders,
        "ReverseProxy:TrustForwardedHeaders must be true in Production behind Azure Container Apps ingress.")
    .ValidateOnStart();

if (reverseProxyConfiguration.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services
    .AddOptions<RxNormOptions>()
    .BindConfiguration(RxNormOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(RxNormOptions.HasValidBaseAddress, "RxNorm:BaseAddress must be a valid HTTPS URL.")
    .Validate(
        RxNormOptions.HasValidTimeoutBudget,
        "RxNorm:TotalRequestTimeoutSeconds must be greater than AttemptTimeoutSeconds.")
    .ValidateOnStart();

string frontendOrigin = builder.Configuration
    .GetRequiredSection(FrontendOptions.SectionName)
    .GetValue<string>(nameof(FrontendOptions.Origin))
    ?? throw new InvalidOperationException("Frontend:Origin configuration is required.");

string? databaseConnectionString = resetDemoData || prepareRelease
    ? builder.Configuration.GetConnectionString("HospitalMaintenanceDatabase")
    : builder.Configuration.GetConnectionString("HospitalDatabase");

if (resetDemoData &&
    string.IsNullOrWhiteSpace(databaseConnectionString) &&
    builder.Environment.IsDevelopment())
{
    databaseConnectionString = builder.Configuration.GetConnectionString("HospitalDatabase");
}

if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException(
        resetDemoData || prepareRelease
            ? "ConnectionStrings:HospitalMaintenanceDatabase configuration is required for maintenance."
            : "ConnectionStrings:HospitalDatabase configuration is required.");
}

RxNormOptions rxNormConfiguration = builder.Configuration
    .GetRequiredSection(RxNormOptions.SectionName)
    .Get<RxNormOptions>()
    ?? throw new InvalidOperationException(
        $"{RxNormOptions.SectionName} configuration is required.");

builder.Services.AddInfrastructure(databaseConnectionString, rxNormConfiguration);
builder.Services.AddApplicationAuthentication(builder.Configuration);
builder.Services.AddApplicationRateLimiting(builder.Configuration);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<ListCliniciansUseCase>();
builder.Services.AddScoped<ListClinicianAvailabilityUseCase>();
builder.Services.AddScoped<ListPatientAppointmentsUseCase>();
builder.Services.AddScoped<BookAppointmentUseCase>();
builder.Services.AddScoped<CancelPatientAppointmentUseCase>();
builder.Services.AddScoped<ListDoctorWorklistUseCase>();
builder.Services.AddScoped<StartConsultationUseCase>();
builder.Services.AddScoped<GetDoctorConsultationUseCase>();
builder.Services.AddScoped<SaveConsultationDraftUseCase>();
builder.Services.AddScoped<CompleteConsultationUseCase>();
builder.Services.AddScoped<SearchMedicationCatalogUseCase>();
builder.Services.AddScoped<IssuePrescriptionUseCase>();
builder.Services.AddScoped<GetDoctorPrescriptionUseCase>();
builder.Services.AddScoped<CancelPrescriptionUseCase>();
builder.Services.AddScoped<ListPatientPrescriptionsUseCase>();
builder.Services.AddScoped<ListPharmacyWorkQueueUseCase>();
builder.Services.AddScoped<GetPharmacyFulfillmentUseCase>();
builder.Services.AddScoped<TransitionFulfillmentUseCase>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(frontendOrigin)
            .WithExposedHeaders("Retry-After")
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

if (prepareRelease)
{
    try
    {
        DemoSeedOptions seedOptions = builder.Configuration.GetRequiredSection(DemoSeedOptions.SectionName)
            .Get<DemoSeedOptions>() ?? throw new InvalidOperationException("Release seed configuration required.");
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        Hospital.Infrastructure.Persistence.ApplicationDbContext context = scope.ServiceProvider
            .GetRequiredService<Hospital.Infrastructure.Persistence.ApplicationDbContext>();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(8));
        await new ReleaseDatabasePreparer(context, scope.ServiceProvider.GetRequiredService<TimeProvider>())
            .PrepareAsync(seedOptions, timeout.Token);
        Console.WriteLine("Release database preparation succeeded: migrations, seed-empty and runtime grants.");
    }
    catch (Exception)
    {
        // No raw connection/SQL/provider error output in production job logs.
        Console.Error.WriteLine("Release database preparation failed. Keep the previous web revision; review before retry.");
        Environment.ExitCode = 1;
    }
    return;
}

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

if (resetDemoData)
{
    try
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(8));
        DemoSeedOptions seedOptions = builder.Configuration
            .GetRequiredSection(DemoSeedOptions.SectionName)
            .Get<DemoSeedOptions>()
            ?? throw new InvalidOperationException(
                $"{DemoSeedOptions.SectionName} configuration is required for demo reset.");
        DemoResetOptions resetOptions = builder.Configuration
            .GetRequiredSection(DemoResetOptions.SectionName)
            .Get<DemoResetOptions>()
            ?? throw new InvalidOperationException(
                $"{DemoResetOptions.SectionName} configuration is required for demo reset.");

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        if (app.Environment.IsProduction())
        {
            Hospital.Infrastructure.Persistence.ApplicationDbContext context = scope.ServiceProvider
                .GetRequiredService<Hospital.Infrastructure.Persistence.ApplicationDbContext>();
            bool permitted = await context.Database.SqlQueryRaw<bool>(
                "SELECT (current_database() = 'hospital_coordination' AND current_user = 'hospital_maintenance') AS \"Value\"")
                .SingleAsync(timeout.Token);
            if (!permitted || resetOptions.ExpectedDatabaseName != "hospital_coordination")
            {
                throw new InvalidOperationException("Production reset requires the dedicated maintenance role and database.");
            }
        }
        DemoDataResetter resetter = scope.ServiceProvider.GetRequiredService<DemoDataResetter>();
        await resetter.ResetAsync(resetOptions, seedOptions, timeout.Token);
        Console.WriteLine("Synthetic demo reset completed and verified.");
    }
    catch (Exception) when (app.Environment.IsProduction())
    {
        Console.Error.WriteLine("Synthetic demo reset failed; inspect maintenance status before retry.");
        Environment.ExitCode = 1;
    }
    return;
}

if (reverseProxyConfiguration.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsProduction())
{
    app.UseHsts();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSpaAssets();
app.UseRouting();
app.UseMiddleware<RouteTemplateCaptureMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
}).AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static registration =>
        registration.Tags.Contains(
            InfrastructureServiceCollectionExtensions.DatabaseReadinessTag),
}).AllowAnonymous();
app.MapControllers();
app.MapSpa();
app.MapFallback("{**path}", static () => Results.NotFound()).AllowAnonymous();

app.Run();

public partial class Program;
