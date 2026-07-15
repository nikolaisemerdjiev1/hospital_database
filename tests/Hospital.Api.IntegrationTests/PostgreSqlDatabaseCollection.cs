namespace Hospital.Api.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlDatabaseTestGroup : ICollectionFixture<PostgreSqlDatabaseFixture>
{
    public const string Name = "PostgreSQL database";
}
