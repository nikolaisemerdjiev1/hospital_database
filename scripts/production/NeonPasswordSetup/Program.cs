using System.Text;

using Npgsql;

namespace Hospital.Tools.NeonPasswordSetup;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string stage = "input";
        try
        {
            if (args.Length != 0 || Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                Console.WriteLine("Run in your own interactive terminal without arguments or redirection.");
                return 1;
            }
            Console.WriteLine("Initializes one existing application role's password; no role creation or data changes.");
            Console.Write("Mode (initialize or verify): ");
            string mode = Console.ReadLine() ?? string.Empty;
            if (mode is not ("initialize" or "verify")) { return 1; }
            Console.Write("Direct Neon hostname only: ");
            string host = PasswordSetup.ValidateHost(Console.ReadLine() ?? string.Empty);
            Console.Write("Role to initialize (hospital_runtime or hospital_maintenance): ");
            string role = Console.ReadLine() ?? string.Empty;
            if (role is not ("hospital_runtime" or "hospital_maintenance"))
            {
                throw new InvalidOperationException("Choose an application role.");
            }
            Console.WriteLine(mode == "initialize"
                ? "Generate and privately save a DIFFERENT 32-128 character password for this role first."
                : "Use the application-role password already saved; this mode only verifies logins.");
            string password = ReadHidden("Application-role password (hidden): ");
            PasswordSetup.ValidatePassword(password);
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
            if (mode == "initialize")
            {
                if (password != ReadHidden("Enter the new password again (hidden): "))
                {
                    throw new InvalidOperationException("Passwords did not match.");
                }
                Console.Write("Existing database owner role name: ");
                string owner = Console.ReadLine() ?? string.Empty;
                if (owner.Length is < 1 or > 63 || owner.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')
                    || owner is "hospital_runtime" or "hospital_maintenance")
                {
                    throw new InvalidOperationException("Invalid owner role name.");
                }
                Console.Write("Type INITIALIZE to set this role's password on hospital_coordination: ");
                if (Console.ReadLine() != "INITIALIZE") { return 1; }
                NpgsqlConnectionStringBuilder ownerConfig = PasswordSetup.Connection(host, owner, ReadHidden("Current OWNER password (hidden): "));
                stage = "owner connection";
                await using (NpgsqlConnection connection = new(ownerConfig.ConnectionString))
                {
                    ownerConfig.Clear();
                    await connection.OpenAsync(timeout.Token);
                    stage = "owner/database/privilege preflight";
                    await PasswordSetup.InitializeAsync(connection, role, password, "hospital_coordination", timeout.Token, value => stage = value);
                }
                Console.WriteLine("Password change acknowledged. Keep the password you just saved.");
            }
            NpgsqlConnectionStringBuilder appConfig = PasswordSetup.Connection(host, role, password);
            try
            {
                stage = "direct login verification";
                await PasswordSetup.VerifyLoginAsync(appConfig, role, timeout.Token);
                Console.WriteLine("PASS: direct login with the new password and expected role.");
                if (role == "hospital_runtime")
                {
                    // Same Neon endpoint with the documented pooler suffix; no arbitrary second host.
                    appConfig.Host = PasswordSetup.LoginHost(host, role);
                    stage = "pooled login verification";
                    await PasswordSetup.VerifyLoginAsync(appConfig, role, timeout.Token);
                    Console.WriteLine("PASS: pooled runtime login with the new password.");
                }
            }
            finally { appConfig.Clear(); }
            Console.WriteLine("Update this role's saved local URL now; leave owner URLs unchanged. See docs/development/neon-access.md.");
            return 0;
        }
        catch (Exception)
        {
            // Driver/server errors can contain SQL or credentials. Deliberately never emit them.
            Console.WriteLine($"STOP: {stage} failed. No automatic retry. Report ONLY this stage.");
            Console.WriteLine("If initialization was attempted, its outcome may be uncertain; keep the entered password and do not reset/recreate roles.");
            return 1;
        }
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        StringBuilder input = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return input.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (input.Length > 0) { input.Length--; } continue; }
            if (char.IsControl(key.KeyChar) || input.Length >= 1024)
            {
                throw new InvalidOperationException("Unsupported secret input.");
            }
            input.Append(key.KeyChar);
        }
    }
}
