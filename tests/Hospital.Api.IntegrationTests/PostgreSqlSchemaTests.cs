using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Pharmacy;
using Hospital.Core.Prescriptions;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Npgsql;

namespace Hospital.Api.IntegrationTests;

[Collection(PostgreSqlDatabaseTestGroup.Name)]
public sealed class PostgreSqlSchemaTests(PostgreSqlDatabaseFixture database)
{
    private static readonly string[] ExpectedTables =
    [
        "appointment",
        "audit_event",
        "availability_slot",
        "clinician_profile",
        "consultation",
        "fulfillment",
        "medication",
        "patient_profile",
        "pharmacist_profile",
        "prescription",
        "user_profile",
    ];

    private static readonly Type[] ConcurrentEntityTypes =
    [
        typeof(AvailabilitySlot),
        typeof(Appointment),
        typeof(Consultation),
        typeof(Prescription),
        typeof(Fulfillment),
    ];

    [Fact]
    public async Task InitialMigrationCreatesExpectedSchemaAndIsRepeatable()
    {
        await database.EnsureMigratedAsync();
        await database.EnsureMigratedAsync();

        await using ApplicationDbContext context = database.CreateContext();
        string[] pendingMigrations =
            [.. await context.Database.GetPendingMigrationsAsync()];
        Assert.Empty(pendingMigrations);

        List<string> actualTables = [];
        await using NpgsqlConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' " +
            "AND table_type = 'BASE TABLE' " +
            "AND table_name <> '__EFMigrationsHistory' " +
            "ORDER BY table_name";
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            actualTables.Add(reader.GetString(0));
        }

        Assert.Equal(ExpectedTables, actualTables);
    }

    [Fact]
    public void WorkflowEntitiesMapVersionToPostgreSqlXmin()
    {
        using ApplicationDbContext context = database.CreateContext();

        foreach (Type entityType in ConcurrentEntityTypes)
        {
            IEntityType mappedType = context.Model.FindEntityType(entityType)
                ?? throw new InvalidOperationException($"{entityType.Name} is not mapped.");
            IProperty version = mappedType.FindProperty("Version")
                ?? throw new InvalidOperationException(
                    $"{entityType.Name} does not expose a Version property.");
            StoreObjectIdentifier table = StoreObjectIdentifier.Table(
                mappedType.GetTableName()
                    ?? throw new InvalidOperationException("The mapped table name is missing."),
                mappedType.GetSchema());

            Assert.Equal("xmin", version.GetColumnName(table));
            Assert.Equal("xid", version.GetColumnType());
            Assert.True(version.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        }
    }

    [Fact]
    public async Task NamedConstraintsProtectWorkflowIntegrityAndHistory()
    {
        await database.EnsureMigratedAsync();
        CareGraph graph = await CreateCareGraphAsync();

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.UniqueViolation,
            "user_profile_auth0_subject_unique",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.UserProfiles.Add(new UserProfile
                {
                    Auth0Subject = graph.PatientSubject,
                    DisplayName = "Duplicate subject",
                    ProfileType = ProfileType.Patient,
                    Status = AccountStatus.Active,
                    CreatedAtUtc = graph.Timestamp,
                });
                await context.SaveChangesAsync();
            });

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.UniqueViolation,
            "medication_rx_cui_unique",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.Medications.Add(new Medication
                {
                    RxCui = graph.RxCui,
                    DisplayName = "Duplicate medication",
                    Source = MedicationSource.SeededFallback,
                    CreatedAtUtc = graph.Timestamp,
                });
                await context.SaveChangesAsync();
            });

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.CheckViolation,
            "availability_slot_time_range_check",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.AvailabilitySlots.Add(new AvailabilitySlot
                {
                    ClinicianProfileId = graph.ClinicianProfileId,
                    StartsAtUtc = graph.Timestamp.AddDays(2),
                    EndsAtUtc = graph.Timestamp.AddDays(2).AddMinutes(-1),
                    CreatedAtUtc = graph.Timestamp,
                });
                await context.SaveChangesAsync();
            });

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.UniqueViolation,
            "availability_slot_clinician_start_unique",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.AvailabilitySlots.Add(new AvailabilitySlot
                {
                    ClinicianProfileId = graph.ClinicianProfileId,
                    StartsAtUtc = graph.Timestamp.AddDays(1),
                    EndsAtUtc = graph.Timestamp.AddDays(1).AddMinutes(60),
                    CreatedAtUtc = graph.Timestamp,
                });
                await context.SaveChangesAsync();
            });

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.UniqueViolation,
            "appointment_active_slot_unique",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.Appointments.Add(CreateAppointment(
                    graph.PatientProfileId,
                    graph.AvailabilitySlotId,
                    graph.Timestamp,
                    "Conflicting active booking"));
                await context.SaveChangesAsync();
            });

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.CheckViolation,
            "consultation_completion_check",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.Consultations.Add(new Consultation
                {
                    AppointmentId = graph.AppointmentId,
                    Outcome = null,
                    ClinicalNotes = null,
                    PatientSummary = null,
                    CareInstructions = null,
                    Status = ConsultationStatus.Completed,
                    StartedAtUtc = graph.Timestamp,
                    CompletedAtUtc = graph.Timestamp.AddMinutes(30),
                    CreatedAtUtc = graph.Timestamp,
                });
                await context.SaveChangesAsync();
            });

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.CheckViolation,
            "appointment_cancellation_check",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.Appointments.Add(new Appointment
                {
                    PatientProfileId = graph.PatientProfileId,
                    AvailabilitySlotId = graph.AvailabilitySlotId,
                    Reason = "Invalid cancellation chronology",
                    Status = AppointmentStatus.Cancelled,
                    CancelledAtUtc = graph.Timestamp.AddMinutes(-1),
                    CancellationReason = "Invalid test state",
                    CreatedAtUtc = graph.Timestamp,
                });
                await context.SaveChangesAsync();
            });

        long consultationId;
        await using (ApplicationDbContext context = database.CreateContext())
        {
            Consultation consultation = CreateCompletedConsultation(
                graph.AppointmentId,
                graph.Timestamp);
            context.Consultations.Add(consultation);
            await context.SaveChangesAsync();
            consultationId = consultation.Id;
        }

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.UniqueViolation,
            "consultation_appointment_id_unique",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.Consultations.Add(new Consultation
                {
                    AppointmentId = graph.AppointmentId,
                    Status = ConsultationStatus.Draft,
                    StartedAtUtc = graph.Timestamp,
                    CreatedAtUtc = graph.Timestamp,
                });
                await context.SaveChangesAsync();
            });

        long prescriptionId;
        await using (ApplicationDbContext context = database.CreateContext())
        {
            Prescription prescription = new()
            {
                ConsultationId = consultationId,
                MedicationId = graph.MedicationId,
                PrescriberClinicianProfileId = graph.ClinicianProfileId,
                PatientProfileId = graph.PatientProfileId,
                RxCuiSnapshot = graph.RxCui,
                MedicationDisplayNameSnapshot = "Test medication",
                Dose = "10 mg",
                Instructions = "Synthetic test instructions",
                Quantity = 10,
                Status = PrescriptionStatus.Issued,
                IssuedAtUtc = graph.Timestamp.AddMinutes(35),
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();
            prescriptionId = prescription.Id;
        }

        await using (ApplicationDbContext context = database.CreateContext())
        {
            context.Fulfillments.Add(new Fulfillment
            {
                PrescriptionId = prescriptionId,
                Status = FulfillmentStatus.Pending,
                CreatedAtUtc = graph.Timestamp.AddMinutes(40),
            });
            await context.SaveChangesAsync();
        }

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.UniqueViolation,
            "fulfillment_prescription_id_unique",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                context.Fulfillments.Add(new Fulfillment
                {
                    PrescriptionId = prescriptionId,
                    Status = FulfillmentStatus.Pending,
                    CreatedAtUtc = graph.Timestamp.AddMinutes(45),
                });
                await context.SaveChangesAsync();
            });

        await using (ApplicationDbContext context = database.CreateContext())
        {
            Appointment appointment = await context.Appointments
                .SingleAsync(item => item.Id == graph.AppointmentId);
            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelledAtUtc = graph.Timestamp.AddHours(1);
            appointment.CancellationReason = "Test cancellation";
            await context.SaveChangesAsync();
        }

        await using (ApplicationDbContext context = database.CreateContext())
        {
            context.Appointments.Add(CreateAppointment(
                graph.PatientProfileId,
                graph.AvailabilitySlotId,
                graph.Timestamp,
                "Allowed rebooking after cancellation"));
            await context.SaveChangesAsync();
        }

        await AssertDatabaseFailureAsync(
            PostgresErrorCodes.RestrictViolation,
            "patient_profile_user_profile_id_fkey",
            async () =>
            {
                await using ApplicationDbContext context = database.CreateContext();
                UserProfile user = await context.UserProfiles
                    .SingleAsync(item => item.Id == graph.PatientUserProfileId);
                context.UserProfiles.Remove(user);
                await context.SaveChangesAsync();
            });
    }

    [Fact]
    public async Task StaleWorkflowUpdateRaisesConcurrencyException()
    {
        await database.EnsureMigratedAsync();
        CareGraph graph = await CreateCareGraphAsync();

        await using ApplicationDbContext firstContext = database.CreateContext();
        await using ApplicationDbContext staleContext = database.CreateContext();
        Appointment first = await firstContext.Appointments
            .SingleAsync(item => item.Id == graph.AppointmentId);
        Appointment stale = await staleContext.Appointments
            .SingleAsync(item => item.Id == graph.AppointmentId);

        first.Reason = "Updated by the first request";
        await firstContext.SaveChangesAsync();

        stale.Reason = "Stale overwrite attempt";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => staleContext.SaveChangesAsync());
    }

    private async Task<CareGraph> CreateCareGraphAsync()
    {
        string marker = Guid.NewGuid().ToString("N")[..8];
        DateTimeOffset timestamp = new(2030, 1, 15, 9, 0, 0, TimeSpan.Zero);
        UserProfile patientUser = new()
        {
            Auth0Subject = $"test-auth|patient-{marker}",
            DisplayName = "Test Patient",
            ProfileType = ProfileType.Patient,
            Status = AccountStatus.Active,
            CreatedAtUtc = timestamp,
        };
        UserProfile clinicianUser = new()
        {
            Auth0Subject = $"test-auth|doctor-{marker}",
            DisplayName = "Test Doctor",
            ProfileType = ProfileType.Doctor,
            Status = AccountStatus.Active,
            CreatedAtUtc = timestamp,
        };

        await using ApplicationDbContext context = database.CreateContext();
        context.UserProfiles.AddRange(patientUser, clinicianUser);
        await context.SaveChangesAsync();

        PatientProfile patient = new()
        {
            UserProfileId = patientUser.Id,
            MedicalRecordNumber = $"MRN-{marker}",
            DateOfBirth = new DateOnly(1990, 1, 1),
            CreatedAtUtc = timestamp,
        };
        ClinicianProfile clinician = new()
        {
            UserProfileId = clinicianUser.Id,
            StaffIdentifier = $"DOC-{marker}",
            Specialty = "Test Medicine",
            CreatedAtUtc = timestamp,
        };
        Medication medication = new()
        {
            RxCui = $"rx-{marker}",
            DisplayName = "Test medication",
            Source = MedicationSource.SeededFallback,
            CreatedAtUtc = timestamp,
        };
        context.PatientProfiles.Add(patient);
        context.ClinicianProfiles.Add(clinician);
        context.Medications.Add(medication);
        await context.SaveChangesAsync();

        AvailabilitySlot slot = new()
        {
            ClinicianProfileId = clinician.Id,
            StartsAtUtc = timestamp.AddDays(1),
            EndsAtUtc = timestamp.AddDays(1).AddMinutes(45),
            CreatedAtUtc = timestamp,
        };
        context.AvailabilitySlots.Add(slot);
        await context.SaveChangesAsync();

        Appointment appointment = CreateAppointment(
            patient.Id,
            slot.Id,
            timestamp,
            "Initial test booking");
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        return new CareGraph(
            patientUser.Auth0Subject,
            patientUser.Id,
            patient.Id,
            clinician.Id,
            medication.Id,
            medication.RxCui,
            slot.Id,
            appointment.Id,
            timestamp);
    }

    private static Appointment CreateAppointment(
        long patientProfileId,
        long availabilitySlotId,
        DateTimeOffset timestamp,
        string reason) =>
        new()
        {
            PatientProfileId = patientProfileId,
            AvailabilitySlotId = availabilitySlotId,
            Reason = reason,
            Status = AppointmentStatus.Scheduled,
            CreatedAtUtc = timestamp,
        };

    private static Consultation CreateCompletedConsultation(
        long appointmentId,
        DateTimeOffset timestamp) =>
        new()
        {
            AppointmentId = appointmentId,
            Outcome = "Test outcome",
            ClinicalNotes = "Synthetic notes",
            PatientSummary = "Synthetic summary",
            CareInstructions = "Synthetic instructions",
            Status = ConsultationStatus.Completed,
            StartedAtUtc = timestamp,
            CompletedAtUtc = timestamp.AddMinutes(30),
            CreatedAtUtc = timestamp,
        };

    private static async Task AssertDatabaseFailureAsync(
        string sqlState,
        string constraintName,
        Func<Task> action)
    {
        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        PostgresException postgresException = Assert.IsType<PostgresException>(
            exception.InnerException);
        Assert.Equal(sqlState, postgresException.SqlState);
        Assert.Equal(constraintName, postgresException.ConstraintName);
    }

    private sealed record CareGraph(
        string PatientSubject,
        long PatientUserProfileId,
        long PatientProfileId,
        long ClinicianProfileId,
        long MedicationId,
        string RxCui,
        long AvailabilitySlotId,
        long AppointmentId,
        DateTimeOffset Timestamp);
}
