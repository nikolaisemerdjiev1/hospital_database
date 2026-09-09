using Microsoft.EntityFrameworkCore;

namespace Hospital.Infrastructure.Persistence.Initialization;

public sealed class DatabaseInitializer(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task InitializeAsync(
        DemoSeedOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        DateOnly currentUtcDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        DateOnly anchorDate = options.ValidateAndGetAnchorDate(currentUtcDate);

        await dbContext.Database.MigrateAsync(cancellationToken);
        await DemoDataSeeder.SeedAsync(dbContext, options, anchorDate, cancellationToken);
    }
}
