using Hospital.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Hospital.Api.IntegrationTests;

public sealed class PostgreSqlDatabaseFixture : IAsyncLifetime
{
    private const string TestConnectionStringVariable = "HOSPITAL_TEST_CONNECTION_STRING";
    private readonly string databaseName = $"hospital_test_{Guid.NewGuid():N}";
    private string? adminConnectionString;
    private bool databaseCreated;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        string sourceConnectionString = Environment.GetEnvironmentVariable(
            TestConnectionStringVariable)
            ?? throw new InvalidOperationException(
                $"{TestConnectionStringVariable} must be set for PostgreSQL integration tests.");

        NpgsqlConnectionStringBuilder adminBuilder = new(sourceConnectionString)
        {
            Database = "postgres",
            Multiplexing = false,
            Pooling = false,
        };
        adminConnectionString = adminBuilder.ConnectionString;

        NpgsqlConnectionStringBuilder databaseBuilder = new(sourceConnectionString)
        {
            Database = databaseName,
            Multiplexing = false,
            Pooling = false,
        };
        ConnectionString = databaseBuilder.ConnectionString;

        await using NpgsqlConnection connection = new(adminConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}";
        await command.ExecuteNonQueryAsync();
        databaseCreated = true;
    }

    public async Task DisposeAsync()
    {
        if (!databaseCreated || adminConnectionString is null)
        {
            return;
        }

        await using NpgsqlConnection connection = new(adminConnectionString);
        await connection.OpenAsync();

        await using (NpgsqlCommand terminateCommand = connection.CreateCommand())
        {
            terminateCommand.CommandText =
                "SELECT pg_terminate_backend(pid) " +
                "FROM pg_stat_activity " +
                "WHERE datname = @database_name AND pid <> pg_backend_pid()";
            terminateCommand.Parameters.AddWithValue("database_name", databaseName);
            await terminateCommand.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand dropCommand = connection.CreateCommand();
        dropCommand.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)}";
        await dropCommand.ExecuteNonQueryAsync();
        databaseCreated = false;
    }

    public ApplicationDbContext CreateContext()
    {
        if (!databaseCreated)
        {
            throw new InvalidOperationException("The leased PostgreSQL database is not available.");
        }

        DbContextOptions<ApplicationDbContext> options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

        return new ApplicationDbContext(options);
    }

    public async Task EnsureMigratedAsync()
    {
        await using ApplicationDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
