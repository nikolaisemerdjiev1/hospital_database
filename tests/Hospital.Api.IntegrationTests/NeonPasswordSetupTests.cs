using Hospital.Tools.NeonPasswordSetup;

using Npgsql;

namespace Hospital.Api.IntegrationTests;

public sealed class NeonPasswordSetupTests
{
    [Theory]
    [InlineData("https://ep-example.us-west-2.aws.neon.tech")]
    [InlineData("ep-example-pooler.us-west-2.aws.neon.tech")]
    [InlineData("ep-example.us-west-2.aws.neon.tech\n")]
    [InlineData("localhost")]
    [InlineData("ep-example.neon.tech.evil.example")]
    public void HostValidationRejectsCredentialsPoolingAndUnexpectedDestinations(string host) =>
        Assert.Throws<InvalidOperationException>(() => PasswordSetup.ValidateHost(host));

    [Theory]
    [InlineData("")]
    [InlineData("too_short")]
    [InlineData("SCRAM-SHA-256$not-a-plaintext-password-verifier")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345\n")]
    public void RejectsInvalidPasswords(string password) =>
        Assert.Throws<InvalidOperationException>(() => PasswordSetup.ValidatePassword(password));

    [Fact]
    public void ProductionConnectionAlwaysVerifiesTlsWithoutLoggingOrPooling()
    {
        NpgsqlConnectionStringBuilder config = PasswordSetup.Connection("ep-example.c-4.us-west-2.aws.neon.tech",
            "hospital_runtime", "synthetic_only");
        Assert.Equal(SslMode.VerifyFull, config.SslMode);
        Assert.Equal(ChannelBinding.Require, config.ChannelBinding);
        Assert.False(config.Pooling);
        Assert.False(config.IncludeErrorDetail);
        Assert.False(config.LogParameters);
        Assert.Equal(15, config.Timeout);
        Assert.Equal(30, config.CommandTimeout);
        Assert.Equal("hospital_coordination", config.Database);
        Assert.Equal("ep-example-pooler.c-4.us-west-2.aws.neon.tech", PasswordSetup.LoginHost(config.Host!, "hospital_runtime"));
        Assert.Equal(config.Host, PasswordSetup.LoginHost(config.Host!, "hospital_maintenance"));
    }

    [Fact]
    public async Task InitializesPasswordlessRolesPreservesPrivilegesAndAuthenticatesWithQuotedPasswords()
    {
        PostgreSqlDatabaseFixture fixture = new();
        await fixture.InitializeAsync();
        string database = new NpgsqlConnectionStringBuilder(fixture.ConnectionString).Database!;
        string owner = $"recovery_{Guid.NewGuid():N}";
        NpgsqlConnectionStringBuilder adminConfig = new(fixture.ConnectionString) { Database = "postgres" };
        await using NpgsqlConnection admin = new(adminConfig.ConnectionString);
        await admin.OpenAsync();
        try
        {
            await ExecuteAsync(admin, $"CREATE ROLE {owner} LOGIN CREATEDB CREATEROLE PASSWORD 'fixture_only'; ALTER DATABASE {database} OWNER TO {owner}");
            NpgsqlConnectionStringBuilder config = new(fixture.ConnectionString)
            {
                Username = owner,
                Password = "fixture_only",
                Pooling = false,
            };
            await using NpgsqlConnection provisioning = new(config.ConnectionString);
            await provisioning.OpenAsync();
            string bootstrap = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ProductionSql", "bootstrap-roles.sql")))
                .Replace("hospital_coordination", database, StringComparison.Ordinal);
            await ExecuteAsync(provisioning, bootstrap);
            string before = await SnapshotAsync(provisioning);
            foreach (string role in new[] { "hospital_runtime", "hospital_maintenance" })
            {
                // Fixed fake values only. Deliberately exercises E-string quote/backslash escaping.
                string password = $"Fixture_ONLY_{role}_aB37'\\;DROP/**/ROLE/**/nobody;--";
                config.Username = role;
                config.Password = password;
                await Assert.ThrowsAsync<PostgresException>(() => PasswordSetup.VerifyLoginAsync(config, role, CancellationToken.None));
                await Assert.ThrowsAsync<InvalidOperationException>(() => PasswordSetup.InitializeAsync(provisioning, role, password, "wrong_database", CancellationToken.None));
                await Assert.ThrowsAsync<InvalidOperationException>(() => PasswordSetup.InitializeAsync(provisioning, owner, password, database, CancellationToken.None));
                await PasswordSetup.InitializeAsync(provisioning, role, password, database, CancellationToken.None);
                await PasswordSetup.VerifyLoginAsync(config, role, CancellationToken.None);
                await using (NpgsqlConnection appLogin = new(config.ConnectionString))
                {
                    await appLogin.OpenAsync();
                    await Assert.ThrowsAsync<InvalidOperationException>(() => PasswordSetup.InitializeAsync(appLogin,
                        role, password, database, CancellationToken.None));
                }
                Assert.Equal(before, await SnapshotAsync(provisioning));
                config.Password = "wrong_fixture_password";
                await Assert.ThrowsAsync<PostgresException>(() => PasswordSetup.VerifyLoginAsync(config, role, CancellationToken.None));
            }
            // Force a server error AFTER the preflight. Its message/context must not
            // expose the bound password or dynamically executed ALTER ROLE statement.
            await ExecuteAsync(admin, $"REVOKE ADMIN OPTION FOR hospital_runtime FROM {owner}");
            const string rejectedSecret = "Fixture_rejected_change_0123456789";
            PostgresException sanitized = await Assert.ThrowsAsync<PostgresException>(() => PasswordSetup.InitializeAsync(provisioning,
                "hospital_runtime", rejectedSecret, database, CancellationToken.None));
            Assert.Equal("P0001", sanitized.SqlState);
            Assert.DoesNotContain(rejectedSecret, sanitized.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("ALTER ROLE", sanitized.ToString(), StringComparison.Ordinal);
            await ExecuteAsync(admin, $"GRANT hospital_runtime TO {owner} WITH ADMIN TRUE, INHERIT FALSE, SET FALSE");
            await ExecuteAsync(admin, $"GRANT SET ON PARAMETER log_statement TO {owner}");
            await ExecuteAsync(provisioning, "SET log_statement = 'all'");
            await Assert.ThrowsAsync<InvalidOperationException>(() => PasswordSetup.InitializeAsync(provisioning,
                "hospital_runtime", rejectedSecret, database, CancellationToken.None));
            await ExecuteAsync(provisioning, "SET log_statement = 'none'");
            await ExecuteAsync(admin, $"REVOKE SET ON PARAMETER log_statement FROM {owner}");
            await ExecuteAsync(provisioning, "GRANT CREATE ON SCHEMA public TO hospital_runtime");
            await Assert.ThrowsAsync<InvalidOperationException>(() => PasswordSetup.InitializeAsync(provisioning,
                "hospital_runtime", "Fixture_rejected_change_0123456789", database, CancellationToken.None));
            await ExecuteAsync(provisioning, "REVOKE CREATE ON SCHEMA public FROM hospital_runtime; CREATE TABLE public.keep_me (id int)");
            await Assert.ThrowsAsync<InvalidOperationException>(() => PasswordSetup.InitializeAsync(provisioning,
                "hospital_runtime", "Fixture_rejected_change_0123456789", database, CancellationToken.None));
            await ExecuteAsync(provisioning, "SELECT * FROM public.keep_me");
        }
        finally
        {
            await fixture.DisposeAsync();
            await ExecuteAsync(admin, $"DROP ROLE IF EXISTS hospital_runtime, hospital_maintenance, {owner}");
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> SnapshotAsync(NpgsqlConnection connection)
    {
        await using NpgsqlCommand command = new("""
            SELECT json_build_object(
              'roles', (SELECT json_agg(r ORDER BY rolname) FROM
                (SELECT oid, rolname, rolsuper, rolinherit, rolcreaterole, rolcreatedb, rolcanlogin,
                        rolreplication, rolbypassrls, rolconnlimit, rolvaliduntil, rolconfig
                 FROM pg_roles WHERE rolname IN ('hospital_runtime','hospital_maintenance')) r),
              'members', (SELECT json_agg(m ORDER BY roleid, member) FROM pg_auth_members m
                          WHERE roleid IN ('hospital_runtime'::regrole, 'hospital_maintenance'::regrole)
                             OR member IN ('hospital_runtime'::regrole, 'hospital_maintenance'::regrole)),
              'schema', (SELECT json_agg(s) FROM (SELECT oid, nspowner, nspacl FROM pg_namespace WHERE nspname='public') s),
              'database', (SELECT json_agg(d) FROM (SELECT datdba, datacl FROM pg_database WHERE datname=current_database()) d)
            )::text
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
