using System.Text.RegularExpressions;

using Npgsql;

namespace Hospital.Tools.NeonPasswordSetup;

internal static partial class PasswordSetup
{
    internal static string ValidateHost(string host)
    {
        if (!NeonHost().IsMatch(host) || host.Split('.')[0].EndsWith("-pooler", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Use the direct Neon hostname only.");
        }
        return host;
    }

    internal static void ValidatePassword(string password)
    {
        // A password-manager-generated, printable ASCII password avoids invisible input
        // and Unicode normalization surprises. This is not an entropy estimator.
        if (password.Length is < 32 or > 128 || password.Any(c => c is < '!' or > '~')
            || password.StartsWith("SCRAM-SHA-256$", StringComparison.Ordinal)
            || password.StartsWith("md5", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Use a new password-manager-generated password: 32-128 printable ASCII characters, no spaces.");
        }
    }

    internal static NpgsqlConnectionStringBuilder Connection(string host, string role, string password) => new()
    {
        Host = ValidateHost(host),
        Database = "hospital_coordination",
        Username = role,
        Password = password,
        SslMode = SslMode.VerifyFull,
        ChannelBinding = ChannelBinding.Require,
        Pooling = false,
        Timeout = 15,
        CommandTimeout = 30,
        IncludeErrorDetail = false,
        LogParameters = false,
        ApplicationName = "hospital-password-setup",
    };

    internal static async Task InitializeAsync(NpgsqlConnection owner, string role, string password,
        string expectedDatabase, CancellationToken cancellationToken, Action<string>? stageChanged = null)
    {
        if (role is not ("hospital_runtime" or "hospital_maintenance"))
        {
            throw new InvalidOperationException("Only the two application roles are supported.");
        }
        ValidatePassword(password);
        // Only public catalog metadata: never read password hashes or application data.
        await using NpgsqlCommand guard = new("""
            SELECT current_database() = $1
              AND current_user = pg_get_userbyid(datdba)
              AND (SELECT count(*) FROM pg_roles WHERE rolname IN ('hospital_runtime','hospital_maintenance')
                   AND rolcanlogin AND NOT (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) = 2
              AND NOT EXISTS (SELECT FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.member
                              WHERE r.rolname IN ('hospital_runtime','hospital_maintenance'))
              AND NOT has_database_privilege('hospital_runtime', current_database(), 'CREATE')
              AND NOT has_database_privilege('hospital_runtime', current_database(), 'TEMPORARY')
              AND NOT has_schema_privilege('hospital_runtime', 'public', 'CREATE')
              AND NOT EXISTS (SELECT FROM pg_class WHERE relnamespace = 'public'::regnamespace)
            FROM pg_database WHERE datname = current_database()
            """, owner);
        guard.Parameters.AddWithValue(expectedDatabase);
        if (await guard.ExecuteScalarAsync(cancellationToken) is not true)
        {
            throw new InvalidOperationException("Owner, empty database or role privilege preflight failed.");
        }

        // The owner can create session-temporary objects. Bind the password to a
        // temporary function, not to a utility statement (ALTER ROLE cannot bind it).
        // Refuse ordinary verbose server logging; never change project-wide settings.
        stageChanged?.Invoke("logging preflight");
        await using NpgsqlCommand logging = new("""
            SELECT current_setting('log_statement') = 'none'
              AND current_setting('log_min_duration_statement') = '-1'
              AND current_setting('log_min_duration_sample') = '-1'
              AND current_setting('log_duration') = 'off'
              AND coalesce(current_setting('pgaudit.log', true), 'none') IN ('', 'none')
              AND coalesce(current_setting('auto_explain.log_min_duration', true), '-1') IN ('', '-1')
              AND coalesce(current_setting('pg_stat_statements.track', true), 'top') <> 'all'
            """, owner);
        if (await logging.ExecuteScalarAsync(cancellationToken) is not true)
        {
            throw new InvalidOperationException("Verbose server logging requires review before password initialization.");
        }
        stageChanged?.Invoke("temporary function preparation");
        await using NpgsqlCommand prepare = new($"""
            SET log_parameter_max_length_on_error = 0;
            CREATE OR REPLACE FUNCTION pg_temp.hospital_initialize_password(new_password text)
            RETURNS void LANGUAGE plpgsql AS $body$
            BEGIN
                EXECUTE format('ALTER ROLE {role} PASSWORD %L', new_password);
            EXCEPTION WHEN OTHERS THEN
                RAISE EXCEPTION 'Password initialization failed' USING ERRCODE = 'P0001';
            END
            $body$;
            """, owner);
        await prepare.ExecuteNonQueryAsync(cancellationToken);
        stageChanged?.Invoke("password initialization (outcome may be uncertain on failure)");
        await using NpgsqlCommand change = new("SELECT pg_temp.hospital_initialize_password($1)", owner);
        change.Parameters.AddWithValue(password);
        try { await change.ExecuteNonQueryAsync(cancellationToken); }
        finally { change.Parameters.Clear(); }
        // The function contains no password and disappears when the owner session closes.
        // No automatic retry: Neon control-plane effects need not roll back with SQL.
    }

    internal static string LoginHost(string directHost, string role)
    {
        ValidateHost(directHost);
        if (role == "hospital_runtime")
        {
            return directHost.Insert(directHost.IndexOf('.', StringComparison.Ordinal), "-pooler");
        }
        return directHost;
    }

    internal static async Task VerifyLoginAsync(NpgsqlConnectionStringBuilder connection, string expectedRole,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection login = new(connection.ConnectionString);
        await login.OpenAsync(cancellationToken);
        await using NpgsqlCommand check = new("SELECT current_user", login);
        if (await check.ExecuteScalarAsync(cancellationToken) is not string actual || actual != expectedRole)
        {
            throw new InvalidOperationException("Unexpected login identity.");
        }
    }

    [GeneratedRegex(@"\Aep-[a-z0-9-]+\.(?:[a-z0-9-]+\.)+neon\.tech\z", RegexOptions.CultureInvariant)]
    private static partial Regex NeonHost();
}
