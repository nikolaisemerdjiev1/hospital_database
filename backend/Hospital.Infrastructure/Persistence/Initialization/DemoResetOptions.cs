namespace Hospital.Infrastructure.Persistence.Initialization;

public sealed class DemoResetOptions
{
    public const string SectionName = "DemoReset";

    public string ExpectedDatabaseName { get; init; } = string.Empty;

    public int LockTimeoutSeconds { get; init; } = 10;

    public int StatementTimeoutSeconds { get; init; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExpectedDatabaseName) ||
            ExpectedDatabaseName.Length > 63 ||
            !string.Equals(
                ExpectedDatabaseName,
                ExpectedDatabaseName.Trim(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "DemoReset:ExpectedDatabaseName must be a non-empty PostgreSQL database name of at most 63 characters without surrounding whitespace.");
        }

        if (LockTimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException(
                "DemoReset:LockTimeoutSeconds must be between 1 and 300.");
        }

        if (StatementTimeoutSeconds is < 5 or > 900)
        {
            throw new InvalidOperationException(
                "DemoReset:StatementTimeoutSeconds must be between 5 and 900.");
        }
    }
}
