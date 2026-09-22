using Microsoft.EntityFrameworkCore;

namespace Hospital.Infrastructure.Persistence.Initialization;

/// <summary>One-shot maintenance operation; never runs as part of HTTP startup.</summary>
public sealed class ReleaseDatabasePreparer(ApplicationDbContext dbContext, TimeProvider timeProvider)
{
    public async Task PrepareAsync(DemoSeedOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ValidateAndGetAnchorDate(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        bool locked = false;
        try
        {
            // No arbitrary database/role parameter in the production entry point.
            bool safe = await dbContext.Database.SqlQueryRaw<bool>("""
                SELECT (current_database() = 'hospital_coordination' AND current_user = 'hospital_maintenance'
                  AND NOT EXISTS (SELECT FROM pg_roles WHERE rolname IN ('hospital_maintenance','hospital_runtime')
                    AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
                  AND (SELECT count(*) FROM pg_roles WHERE rolname IN ('hospital_maintenance','hospital_runtime') AND rolcanlogin) = 2
                  AND NOT EXISTS (SELECT FROM pg_auth_members WHERE member IN ('hospital_maintenance'::regrole,'hospital_runtime'::regrole))
                  AND NOT EXISTS (SELECT FROM pg_class WHERE relnamespace='public'::regnamespace
                    AND relkind IN ('r','p','S','v','m','f') AND relowner <> current_user::regrole)
                  AND NOT has_database_privilege('hospital_runtime',current_database(),'CREATE')
                  AND NOT has_database_privilege('hospital_runtime',current_database(),'TEMPORARY')
                  AND NOT has_schema_privilege('hospital_runtime','public','CREATE')) AS "Value"
                """).SingleAsync(cancellationToken);
            if (!safe)
            {
                throw new InvalidOperationException("Release maintenance database/role/ownership preflight failed.");
            }
            locked = await dbContext.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_lock(7239061401) AS \"Value\"").SingleAsync(cancellationToken);
            if (!locked) { throw new InvalidOperationException("Another release maintenance session is active."); }

            // The existing initializer migrates and only seeds an empty database.
            // It does not reset existing workflows. EF migrations are carried in this image.
            await new DatabaseInitializer(dbContext, timeProvider).InitializeAsync(options, cancellationToken);
            using Stream stream = typeof(ReleaseDatabasePreparer).Assembly.GetManifestResourceStream("Hospital.Release.grant-runtime.sql")
                ?? throw new InvalidOperationException("Release grant resource missing.");
            using StreamReader reader = new(stream);
            string sql = await reader.ReadToEndAsync(cancellationToken);
            // Trusted embedded SQL only, no user input. The script owns its grant transaction.
            await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        finally
        {
            try
            {
                if (locked)
                {
                    // A failed grant script may leave its explicit transaction aborted.
                    await dbContext.Database.ExecuteSqlRawAsync("ROLLBACK; SELECT pg_advisory_unlock(7239061401)", CancellationToken.None);
                }
            }
            finally { await dbContext.Database.CloseConnectionAsync(); }
        }
    }
}
