using Hospital.Core.Audit;
using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Pharmacy;
using Hospital.Core.Prescriptions;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;
using Hospital.Infrastructure.Persistence.Initialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Hospital.Api.IntegrationTests;

public sealed class ProductionPrivilegeTests
{
    [Fact]
    public async Task ScriptsSupportMigrationOwnerAndRestrictedRuntimeWithoutOverwritingExistingData()
    {
        PostgreSqlDatabaseFixture fixture = new();
        await fixture.InitializeAsync();
        string suffix = Guid.NewGuid().ToString("N");
        string owner = $"setup_{suffix}";
        string maintenance = $"maint_{suffix}";
        string runtime = $"runtime_{suffix}";
        string database = new NpgsqlConnectionStringBuilder(fixture.ConnectionString).Database!;
        await using NpgsqlConnection admin = new(fixture.ConnectionString);
        await admin.OpenAsync();
        try
        {
            // Synthetic fixture credentials only; CI requires actual password authentication.
            await ExecuteAsync(admin, $"CREATE ROLE {owner} LOGIN CREATEDB CREATEROLE PASSWORD 'fixture_only'; ALTER DATABASE {database} OWNER TO {owner}");
            string bootstrap = await LoadSqlAsync("bootstrap-roles.sql", database, maintenance, runtime);
            string grants = await LoadSqlAsync("grant-runtime.sql", database, maintenance, runtime);
            await using (NpgsqlConnection provisioning = new(ForRole(fixture, owner)))
            {
                await provisioning.OpenAsync();
                // A wrong target or existing table must leave roles and data untouched.
                await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(provisioning,
                    bootstrap.Replace($"current_database() <> '{database}'", "current_database() <> 'wrong'", StringComparison.Ordinal)));
                await ExecuteAsync(provisioning, "ROLLBACK; CREATE TABLE public.existing_data (id int)");
                await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(provisioning, bootstrap));
                await ExecuteAsync(provisioning, "ROLLBACK; DROP TABLE public.existing_data");
                await ExecuteAsync(provisioning, bootstrap);
                await ExecuteAsync(provisioning, await LoadSqlAsync("verify-roles.sql", database, maintenance, runtime));
                await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(provisioning, bootstrap));
                await ExecuteAsync(provisioning, "ROLLBACK");
            }

            await ExecuteAsync(admin, $"ALTER ROLE {maintenance} PASSWORD 'fixture_only'; ALTER ROLE {runtime} PASSWORD 'fixture_only'");
            await using ApplicationDbContext migrationContext = CreateContext(ForRole(fixture, maintenance));
            await new DatabaseInitializer(migrationContext, new FixedTimeProvider(AuthTestClock.UtcNow))
                .InitializeAsync(new DemoSeedOptions
                {
                    AnchorDate = "today",
                    Subjects = new DemoIdentitySubjects
                    {
                        Patient = "fixture|patient",
                        Doctor = "fixture|doctor",
                        Pharmacist = "fixture|pharmacist",
                        Administrator = "fixture|admin",
                    },
                });
            await using NpgsqlConnection maint = new(ForRole(fixture, maintenance));
            await maint.OpenAsync();
            await ExecuteAsync(admin, $"GRANT CREATE ON SCHEMA public TO {runtime}");
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(maint, grants));
            await ExecuteAsync(maint, "ROLLBACK");
            await ExecuteAsync(admin, $"REVOKE CREATE ON SCHEMA public FROM {runtime}");
            await ExecuteAsync(maint, grants);
            await VerifyCareJourneyAsync(ForRole(fixture, runtime));
            // Reapplication must remove accidentally added column-level audit access.
            await ExecuteAsync(maint, $"GRANT SELECT(metadata_json) ON audit_event TO {runtime}");
            await ExecuteAsync(maint, grants);

            await using NpgsqlConnection app = new(ForRole(fixture, runtime));
            await app.OpenAsync();
            await ExecuteAsync(app, await LoadSqlAsync("verify-roles.sql", database, maintenance, runtime));
            foreach (string table in new[] { "user_profile", "patient_profile", "clinician_profile", "pharmacist_profile", "availability_slot", "appointment", "consultation", "medication", "prescription", "fulfillment" })
            {
                await ExecuteAsync(app, $"SELECT * FROM {table} LIMIT 1");
            }
            foreach ((string table, string column) in new[] { ("appointment", "reason"), ("consultation", "clinical_notes"), ("medication", "display_name"), ("prescription", "instructions"), ("fulfillment", "status") })
            {
                await ExecuteAsync(app, $"UPDATE {table} SET {column} = {column} WHERE false");
            }
            await using (ApplicationDbContext runtimeContext = CreateContext(ForRole(fixture, runtime)))
            {
                AuditEvent audit = new() { Action = "PrivilegeTest", AffectedEntityType = "Fixture", OccurredAtUtc = DateTimeOffset.UtcNow };
                Medication medication = new() { RxCui = "fixture-privilege", DisplayName = "Fixture", Source = MedicationSource.SeededFallback, CreatedAtUtc = DateTimeOffset.UtcNow };
                runtimeContext.AuditEvents.Add(audit);
                runtimeContext.Medications.Add(medication);
                await runtimeContext.SaveChangesAsync();
                Assert.True(audit.Id > 0);
                Assert.True(medication.Id > 0);
            }
            foreach (string denied in new[] {
                "SELECT metadata_json FROM audit_event", "UPDATE audit_event SET action = 'bad'", "DELETE FROM audit_event",
                "SELECT * FROM \"__EFMigrationsHistory\"", "INSERT INTO \"__EFMigrationsHistory\" VALUES ('bad','bad')",
                "TRUNCATE medication RESTART IDENTITY", "DELETE FROM medication", "UPDATE user_profile SET display_name = 'bad'",
                "CREATE TABLE public.forbidden (id int)", "CREATE TEMP TABLE forbidden (id int)", "CREATE SCHEMA forbidden",
                "SELECT setval('medication_id_seq', 100)", $"SET ROLE {maintenance}", $"SET ROLE {owner}" })
            {
                PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(app, denied));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            await ExecuteAsync(maint, "CREATE TABLE public.future_private (id int); INSERT INTO future_private VALUES (1)");
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(app, "SELECT * FROM future_private"));
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(maint, grants));
            await ExecuteAsync(maint, "ROLLBACK; DROP TABLE public.future_private");
            await ExecuteAsync(maint, "CREATE FUNCTION public.future_private() RETURNS integer LANGUAGE sql AS 'SELECT 1'");
            PostgresException functionError = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(app, "SELECT public.future_private()"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, functionError.SqlState);
            await ExecuteAsync(maint, "DROP FUNCTION public.future_private()");
            // The maintenance owner can perform the required reset operation in this disposable fixture.
            await ExecuteAsync(maint, "TRUNCATE audit_event RESTART IDENTITY");
        }
        finally
        {
            await admin.CloseAsync();
            await fixture.DisposeAsync();
            NpgsqlConnectionStringBuilder cleanup = new(fixture.ConnectionString) { Database = "postgres" };
            await using NpgsqlConnection connection = new(cleanup.ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"DROP ROLE IF EXISTS {runtime}; DROP ROLE IF EXISTS {maintenance}; DROP ROLE IF EXISTS {owner}");
        }
    }

    private static string ForRole(PostgreSqlDatabaseFixture fixture, string role) =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Username = role, Password = "fixture_only", Pooling = false }.ConnectionString;

    private static async Task VerifyCareJourneyAsync(string connection)
    {
        using AuthenticationApiFactory factory = new(connection);
        using IServiceScope scope = factory.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        ApplicationDbContext context = services.GetRequiredService<ApplicationDbContext>();
        long patient = await context.UserProfiles.Where(row => row.Auth0Subject == "fixture|patient").Select(row => row.Id).SingleAsync();
        long doctor = await context.UserProfiles.Where(row => row.Auth0Subject == "fixture|doctor").Select(row => row.Id).SingleAsync();
        long pharmacist = await context.UserProfiles.Where(row => row.Auth0Subject == "fixture|pharmacist").Select(row => row.Id).SingleAsync();
        AvailabilitySlot slot = await context.AvailabilitySlots
            .Where(row => row.ClinicianProfile.UserProfileId == doctor && row.StartsAtUtc > AuthTestClock.UtcNow)
            .Where(row => !context.Appointments.Any(appointment => appointment.Status != AppointmentStatus.Cancelled
                && appointment.AvailabilitySlot.StartsAtUtc < row.EndsAtUtc && appointment.AvailabilitySlot.EndsAtUtc > row.StartsAtUtc))
            .OrderBy(row => row.StartsAtUtc).FirstAsync();
        var booked = await services.GetRequiredService<BookAppointmentUseCase>()
            .ExecuteAsync(patient, slot.Id, slot.Version, "Fixture booking");
        Assert.True(booked.IsSuccess, booked.ErrorCode);
        var started = await services.GetRequiredService<StartConsultationUseCase>()
            .ExecuteAsync(doctor, booked.Value!.Id, booked.Value.Version, "privilege-fixture");
        Assert.True(started.IsSuccess, started.ErrorCode);
        var completed = await services.GetRequiredService<CompleteConsultationUseCase>()
            .ExecuteAsync(doctor, started.Value!.Id, "Fixture outcome", "Fixture notes", "Fixture summary",
                "Fixture care instructions", started.Value.Version, "privilege-fixture");
        Assert.True(completed.IsSuccess, completed.ErrorCode);
        long medication = await context.Medications.Select(row => row.Id).FirstAsync();
        var issued = await services.GetRequiredService<IssuePrescriptionUseCase>()
            .ExecuteAsync(doctor, completed.Value!.Id, medication, "Fixture dose", "Fixture instructions", 5, "privilege-fixture");
        Assert.True(issued.IsSuccess, issued.ErrorCode);
        foreach (FulfillmentStatus status in new[] { FulfillmentStatus.InReview, FulfillmentStatus.Ready, FulfillmentStatus.Dispensed })
        {
            context.ChangeTracker.Clear();
            Fulfillment fulfillment = await context.Fulfillments.SingleAsync(row => row.PrescriptionId == issued.Value!.Id);
            var transitioned = await services.GetRequiredService<TransitionFulfillmentUseCase>()
                .ExecuteAsync(pharmacist, fulfillment.Id, status, fulfillment.Version, "privilege-fixture");
            Assert.True(transitioned.IsSuccess, transitioned.ErrorCode);
        }
    }

    private static ApplicationDbContext CreateContext(string connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);

    private static async Task<string> LoadSqlAsync(string file, string database, string maintenance, string runtime) =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ProductionSql", file)))
            .Replace("hospital_coordination", database, StringComparison.Ordinal)
            .Replace("hospital_maintenance", maintenance, StringComparison.Ordinal)
            .Replace("hospital_runtime", runtime, StringComparison.Ordinal);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
