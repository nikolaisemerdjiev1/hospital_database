using Hospital.Core.Profiles;
using Hospital.Infrastructure.Persistence;
using Hospital.Infrastructure.Persistence.Initialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace Hospital.Api.IntegrationTests;

[Collection(PostgreSqlDatabaseTestGroup.Name)]
public sealed class DemoResetTests
{
    private static readonly DateTimeOffset InitialUtcNow =
        new(2030, 2, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResetUtcNow =
        new(2031, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResetRestoresCanonicalDatasetAndPreservesMigrationHistory()
    {
        PostgreSqlDatabaseFixture database = new();
        await database.InitializeAsync();

        try
        {
            await InitializeAsync(database);
            string[] appliedMigrations;

            await using (ApplicationDbContext context = database.CreateContext())
            {
                appliedMigrations = [.. await context.Database.GetAppliedMigrationsAsync()];
                UserProfile patient = await context.UserProfiles.SingleAsync(
                    profile => profile.Auth0Subject == "test-auth|patient");
                patient.DisplayName = "Changed by a public demo visitor";
                await context.SaveChangesAsync();
            }

            await ResetAsync(database);
            await AssertCanonicalResetStateAsync(database, appliedMigrations);

            await ResetAsync(database);
            await AssertCanonicalResetStateAsync(database, appliedMigrations);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task IncorrectExpectedDatabaseFailsBeforeChangingData()
    {
        PostgreSqlDatabaseFixture database = new();
        await database.InitializeAsync();

        try
        {
            await InitializeAsync(database);

            await using (ApplicationDbContext context = database.CreateContext())
            {
                DemoDataResetter resetter = CreateResetter(context);
                DemoResetOptions options = CreateResetOptions(database);
                options = new DemoResetOptions
                {
                    ExpectedDatabaseName = $"{options.ExpectedDatabaseName}_wrong",
                    LockTimeoutSeconds = options.LockTimeoutSeconds,
                    StatementTimeoutSeconds = options.StatementTimeoutSeconds,
                };

                InvalidOperationException exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => resetter.ResetAsync(options, CreateSeedOptions("today")));
                Assert.Contains(
                    "does not match DemoReset:ExpectedDatabaseName",
                    exception.Message,
                    StringComparison.Ordinal);
            }

            await using ApplicationDbContext verificationContext = database.CreateContext();
            Assert.Equal(51, await verificationContext.UserProfiles.CountAsync());
            Assert.Equal(46, await verificationContext.AuditEvents.CountAsync());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task SeedFailureRollsBackTruncationAndPreservesPriorDataset()
    {
        PostgreSqlDatabaseFixture database = new();
        await database.InitializeAsync();

        try
        {
            await InitializeAsync(database);
            await using (ApplicationDbContext context = database.CreateContext())
            {
                UserProfile patient = await context.UserProfiles.SingleAsync(
                    profile => profile.Auth0Subject == "test-auth|patient");
                patient.DisplayName = "State that must survive rollback";
                await context.SaveChangesAsync();
            }

            DemoSeedOptions conflictingSeedOptions = new()
            {
                AnchorDate = "today",
                Subjects = new DemoIdentitySubjects
                {
                    Patient = "demo-seed|patient-002",
                    Doctor = "test-auth|doctor",
                    Pharmacist = "test-auth|pharmacist",
                    Administrator = "test-auth|administrator",
                },
            };

            await using (ApplicationDbContext context = database.CreateContext())
            {
                DemoDataResetter resetter = CreateResetter(context);
                InvalidOperationException exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => resetter.ResetAsync(
                            CreateResetOptions(database),
                            conflictingSeedOptions));
                Assert.Contains(
                    "conflict with a deterministic synthetic subject",
                    exception.Message,
                    StringComparison.Ordinal);
            }

            await using ApplicationDbContext verificationContext = database.CreateContext();
            UserProfile preservedPatient = await verificationContext.UserProfiles.SingleAsync(
                profile => profile.Auth0Subject == "test-auth|patient");
            Assert.Equal("State that must survive rollback", preservedPatient.DisplayName);
            Assert.Equal(51, await verificationContext.UserProfiles.CountAsync());
            Assert.Equal(46, await verificationContext.AuditEvents.CountAsync());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentResetsSerializeAndLeaveOneCanonicalDataset()
    {
        PostgreSqlDatabaseFixture database = new();
        await database.InitializeAsync();

        try
        {
            await InitializeAsync(database);
            TaskCompletionSource start = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task firstReset = RunResetAfterSignalAsync(database, start.Task);
            Task secondReset = RunResetAfterSignalAsync(database, start.Task);
            start.SetResult();

            await Task.WhenAll(firstReset, secondReset);

            await using ApplicationDbContext context = database.CreateContext();
            Assert.Equal(51, await context.UserProfiles.CountAsync());
            Assert.Equal(46, await context.AuditEvents.CountAsync());
            Assert.Equal(
                1,
                await context.AuditEvents.CountAsync(
                    auditEvent => auditEvent.Action == "DemoDataSeeded"));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public void ResetOptionsRejectUnsafeGuardAndTimeoutValues()
    {
        DemoResetOptions options = new()
        {
            ExpectedDatabaseName = " ",
            LockTimeoutSeconds = 0,
            StatementTimeoutSeconds = 1,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            options.Validate);

        Assert.Contains("ExpectedDatabaseName", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MismatchedMigrationHistoryRefusesBeforeTruncation(bool newerDatabase)
    {
        PostgreSqlDatabaseFixture database = new();
        await database.InitializeAsync();
        try
        {
            await InitializeAsync(database);
            await using ApplicationDbContext context = database.CreateContext();
            await context.Database.ExecuteSqlRawAsync(newerDatabase
                ? "INSERT INTO \"__EFMigrationsHistory\" VALUES ('99999999999999_Unknown', '10.0.0')"
                : "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = (SELECT max(\"MigrationId\") FROM \"__EFMigrationsHistory\")");
            string[] history = [.. await context.Database.GetAppliedMigrationsAsync()];
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateResetter(context).ResetAsync(CreateResetOptions(database), CreateSeedOptions("today")));
            Assert.Contains("match exactly", error.Message, StringComparison.Ordinal);
            Assert.Equal(history, await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(51, await context.UserProfiles.CountAsync());
            Assert.Equal(46, await context.AuditEvents.CountAsync());
            Assert.Equal(new DateTimeOffset(2030, 2, 15, 8, 0, 0, TimeSpan.Zero),
                await context.AuditEvents.Where(row => row.Action == "DemoDataSeeded").Select(row => row.OccurredAtUtc).SingleAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task InitializeAsync(PostgreSqlDatabaseFixture database)
    {
        await using ApplicationDbContext context = database.CreateContext();
        DatabaseInitializer initializer = new(
            context,
            new FixedTimeProvider(InitialUtcNow));
        await initializer.InitializeAsync(CreateSeedOptions("today"));
    }

    private static async Task ResetAsync(PostgreSqlDatabaseFixture database)
    {
        await using ApplicationDbContext context = database.CreateContext();
        DemoDataResetter resetter = CreateResetter(context);
        await resetter.ResetAsync(
            CreateResetOptions(database),
            CreateSeedOptions("today"));
    }

    private static async Task RunResetAfterSignalAsync(
        PostgreSqlDatabaseFixture database,
        Task start)
    {
        await start;
        await ResetAsync(database);
    }

    private static DemoDataResetter CreateResetter(ApplicationDbContext context) =>
        new(
            context,
            new FixedTimeProvider(ResetUtcNow),
            NullLogger<DemoDataResetter>.Instance);

    private static DemoResetOptions CreateResetOptions(PostgreSqlDatabaseFixture database) =>
        new()
        {
            ExpectedDatabaseName = new NpgsqlConnectionStringBuilder(database.ConnectionString)
                .Database
                ?? throw new InvalidOperationException("Test database name is required."),
            LockTimeoutSeconds = 30,
            StatementTimeoutSeconds = 120,
        };

    private static DemoSeedOptions CreateSeedOptions(string anchorDate) =>
        new()
        {
            AnchorDate = anchorDate,
            Subjects = new DemoIdentitySubjects
            {
                Patient = "test-auth|patient",
                Doctor = "test-auth|doctor",
                Pharmacist = "test-auth|pharmacist",
                Administrator = "test-auth|administrator",
            },
        };

    private static async Task AssertCanonicalResetStateAsync(
        PostgreSqlDatabaseFixture database,
        string[] expectedAppliedMigrations)
    {
        await using ApplicationDbContext context = database.CreateContext();

        Assert.Equal(expectedAppliedMigrations, await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(51, await context.UserProfiles.CountAsync());
        Assert.Equal(36, await context.Appointments.CountAsync());
        Assert.Equal(9, await context.Prescriptions.CountAsync());
        Assert.Equal(9, await context.Fulfillments.CountAsync());
        Assert.Equal(46, await context.AuditEvents.CountAsync());
        Assert.Equal(
            "Avery Brooks",
            await context.UserProfiles
                .Where(profile => profile.Auth0Subject == "test-auth|patient")
                .Select(profile => profile.DisplayName)
                .SingleAsync());
        Assert.Equal(
            new DateTimeOffset(2031, 6, 12, 8, 0, 0, TimeSpan.Zero),
            await context.AuditEvents
                .Where(auditEvent => auditEvent.Action == "DemoDataSeeded")
                .Select(auditEvent => auditEvent.OccurredAtUtc)
                .SingleAsync());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
