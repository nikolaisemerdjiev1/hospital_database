using Hospital.Core.Medications;
using Hospital.Core.Persistence;
using Hospital.Infrastructure.Medications;
using Hospital.Infrastructure.Persistence;
using Hospital.Infrastructure.Persistence.Initialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using Polly;

namespace Hospital.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string DatabaseReadinessTag = "ready";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string databaseConnectionString,
        RxNormOptions? rxNormConfiguration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseConnectionString);

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(databaseConnectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IApplicationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<IApplicationTransaction, EfApplicationTransaction>();
        services.AddMemoryCache(options => options.SizeLimit = 2_000);
        RxNormOptions resilienceOptions = rxNormConfiguration ?? new RxNormOptions();
        services.AddHttpClient<RxNormClient>((serviceProvider, client) =>
            {
                RxNormOptions options = serviceProvider
                    .GetRequiredService<IOptions<RxNormOptions>>()
                    .Value;
                client.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "HospitalCoordinationPortfolio/1.0");
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = resilienceOptions.RetryAttempts;
                options.Retry.Delay = TimeSpan.FromMilliseconds(250);
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(
                    resilienceOptions.AttemptTimeoutSeconds);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(
                    resilienceOptions.TotalRequestTimeoutSeconds);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.FailureRatio = 0.5;
            });
        services.AddScoped<IMedicationCatalog, RxNormMedicationCatalog>();
        services.AddScoped<DatabaseInitializer>();

        services
            .AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>(
                name: "postgresql",
                tags: [DatabaseReadinessTag]);

        return services;
    }
}
