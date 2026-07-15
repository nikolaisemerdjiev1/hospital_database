using Hospital.Core.Persistence;
using Hospital.Infrastructure.Persistence;
using Hospital.Infrastructure.Persistence.Initialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hospital.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string DatabaseReadinessTag = "ready";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string databaseConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseConnectionString);

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(databaseConnectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IApplicationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<DatabaseInitializer>();

        services
            .AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>(
                name: "postgresql",
                tags: [DatabaseReadinessTag]);

        return services;
    }
}
