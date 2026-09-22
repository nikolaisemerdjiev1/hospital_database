using System.Data.Common;
using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Hospital.Infrastructure.Persistence.Initialization;

public sealed partial class DemoDataResetter(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<DemoDataResetter> logger)
{
    private const string TruncateDemoTablesSql =
        """
        TRUNCATE TABLE
            audit_event,
            fulfillment,
            prescription,
            consultation,
            appointment,
            availability_slot,
            pharmacist_profile,
            clinician_profile,
            patient_profile,
            medication,
            user_profile
        RESTART IDENTITY RESTRICT
        """;

    public async Task ResetAsync(
        DemoResetOptions resetOptions,
        DemoSeedOptions seedOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resetOptions);
        ArgumentNullException.ThrowIfNull(seedOptions);

        resetOptions.Validate();
        DateOnly currentUtcDate = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        DateOnly anchorDate = seedOptions.ValidateAndGetAnchorDate(currentUtcDate);

        try
        {
            LogResetStarted(logger, anchorDate);

            await using IDbContextTransaction transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);

            string lockTimeout = string.Create(
                CultureInfo.InvariantCulture,
                $"{resetOptions.LockTimeoutSeconds}s");
            string statementTimeout = string.Create(
                CultureInfo.InvariantCulture,
                $"{resetOptions.StatementTimeoutSeconds}s");
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('lock_timeout', {lockTimeout}, true)",
                cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('statement_timeout', {statementTimeout}, true)",
                cancellationToken);

            string currentDatabase = await GetCurrentDatabaseNameAsync(cancellationToken);
            if (!string.Equals(
                    currentDatabase,
                    resetOptions.ExpectedDatabaseName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The connected database does not match DemoReset:ExpectedDatabaseName. No demo data was changed.");
            }

            // Shared with ReleaseDatabasePreparer's session lock. Acquire BEFORE inspecting
            // migration history so a queued reset cannot validate an obsolete schema.
            await dbContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(7239061401)", cancellationToken);
            string[] appliedMigrations =
                [.. await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)];
            if (!appliedMigrations.Order(StringComparer.Ordinal).SequenceEqual(
                    dbContext.Database.GetMigrations().Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Demo data cannot be reset unless the image and database migration histories match exactly.");
            }

            await DemoDataSeeder.AcquireAdvisoryLockAsync(dbContext, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                TruncateDemoTablesSql,
                cancellationToken);
            dbContext.ChangeTracker.Clear();

            await DemoDataSeeder.SeedWithinCurrentTransactionAsync(
                dbContext,
                seedOptions,
                anchorDate,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            LogResetCompleted(logger, anchorDate);
        }
        catch (Exception exception)
        {
            LogResetFailed(logger, anchorDate, exception);
            throw;
        }
    }

    private async Task<string> GetCurrentDatabaseNameAsync(CancellationToken cancellationToken)
    {
        DbConnection connection = dbContext.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT current_database()";
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string ?? throw new InvalidOperationException(
            "PostgreSQL did not return the current database name.");
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Synthetic demo reset started for UTC anchor {AnchorDate}")]
    private static partial void LogResetStarted(ILogger logger, DateOnly anchorDate);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Synthetic demo reset completed for UTC anchor {AnchorDate}")]
    private static partial void LogResetCompleted(ILogger logger, DateOnly anchorDate);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "Synthetic demo reset failed for UTC anchor {AnchorDate}")]
    private static partial void LogResetFailed(
        ILogger logger,
        DateOnly anchorDate,
        Exception exception);
}
