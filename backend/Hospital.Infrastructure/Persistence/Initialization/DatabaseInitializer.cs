using Microsoft.EntityFrameworkCore;

namespace Hospital.Infrastructure.Persistence.Initialization;

public sealed class DatabaseInitializer(ApplicationDbContext dbContext)
{
    public async Task InitializeAsync(
        DemoSeedOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        DateOnly anchorDate = options.ValidateAndGetAnchorDate();

        await dbContext.Database.MigrateAsync(cancellationToken);
        await DemoDataSeeder.SeedAsync(dbContext, options, anchorDate, cancellationToken);
    }
}
