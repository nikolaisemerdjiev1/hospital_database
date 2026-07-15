using Hospital.Core.Audit;
using Hospital.Core.Medications;
using Hospital.Core.Prescriptions;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;
using Hospital.Infrastructure.Persistence.Initialization;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Api.IntegrationTests;

[Collection(PostgreSqlDatabaseTestGroup.Name)]
public sealed class DemoSeedTests
{
    [Fact]
    public async Task InitializerSeedsDeterministicDatasetAndIsIdempotent()
    {
        PostgreSqlDatabaseFixture database = new();
        await database.InitializeAsync();

        try
        {
            DemoSeedOptions options = CreateOptions();
            await using (ApplicationDbContext context = database.CreateContext())
            {
                DatabaseInitializer initializer = new(context);
                await initializer.InitializeAsync(options);
            }

            await AssertExpectedDatasetAsync(database);

            await using (ApplicationDbContext context = database.CreateContext())
            {
                DatabaseInitializer initializer = new(context);
                await initializer.InitializeAsync(options);
            }

            await AssertExpectedDatasetAsync(database);

            await using (ApplicationDbContext context = database.CreateContext())
            {
                AuditEvent removableAuditEvent = await context.AuditEvents
                    .FirstAsync(auditEvent => auditEvent.Action != "DemoDataSeeded");
                context.AuditEvents.Remove(removableAuditEvent);
                await context.SaveChangesAsync();
            }

            await using (ApplicationDbContext context = database.CreateContext())
            {
                DatabaseInitializer initializer = new(context);
                InvalidOperationException exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => initializer.InitializeAsync(options));
                Assert.Contains(
                    "does not match a complete v1 dataset",
                    exception.Message,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public void SeedOptionsRejectDuplicateSubjectsBeforeDatabaseWork()
    {
        DemoSeedOptions options = CreateOptions();
        options = new DemoSeedOptions
        {
            AnchorDate = options.AnchorDate,
            Subjects = new DemoIdentitySubjects
            {
                Patient = options.Subjects.Patient,
                Doctor = options.Subjects.Patient,
                Pharmacist = options.Subjects.Pharmacist,
                Administrator = options.Subjects.Administrator,
            },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => _ = options.ValidateAndGetAnchorDate());
        Assert.Contains("must be unique", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeedOptionsRejectWhitespaceAroundOpaqueSubjects()
    {
        DemoSeedOptions options = CreateOptions();
        options = new DemoSeedOptions
        {
            AnchorDate = options.AnchorDate,
            Subjects = new DemoIdentitySubjects
            {
                Patient = $"{options.Subjects.Patient} ",
                Doctor = options.Subjects.Doctor,
                Pharmacist = options.Subjects.Pharmacist,
                Administrator = options.Subjects.Administrator,
            },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => _ = options.ValidateAndGetAnchorDate());
        Assert.Contains("whitespace", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializerRefusesPartiallyPopulatedDatabaseWithoutChangingIt()
    {
        PostgreSqlDatabaseFixture database = new();
        await database.InitializeAsync();

        try
        {
            await database.EnsureMigratedAsync();
            await using (ApplicationDbContext context = database.CreateContext())
            {
                context.Medications.Add(new Medication
                {
                    RxCui = "partial-test",
                    DisplayName = "Existing medication",
                    Source = MedicationSource.SeededFallback,
                    CreatedAtUtc = new DateTimeOffset(
                        2030,
                        1,
                        1,
                        0,
                        0,
                        0,
                        TimeSpan.Zero),
                });
                await context.SaveChangesAsync();
            }

            await using (ApplicationDbContext context = database.CreateContext())
            {
                DatabaseInitializer initializer = new(context);
                InvalidOperationException exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => initializer.InitializeAsync(CreateOptions()));
                Assert.Contains(
                    "only initialize an empty database",
                    exception.Message,
                    StringComparison.Ordinal);
            }

            await using (ApplicationDbContext context = database.CreateContext())
            {
                Assert.Equal(1, await context.Medications.CountAsync());
                Assert.Empty(await context.UserProfiles.ToListAsync());
                Assert.Empty(await context.AuditEvents.ToListAsync());
            }
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static DemoSeedOptions CreateOptions() =>
        new()
        {
            AnchorDate = "2030-02-15",
            Subjects = new DemoIdentitySubjects
            {
                Patient = "test-auth|patient",
                Doctor = "test-auth|doctor",
                Pharmacist = "test-auth|pharmacist",
                Administrator = "test-auth|administrator",
            },
        };

    private static async Task AssertExpectedDatasetAsync(PostgreSqlDatabaseFixture database)
    {
        await using ApplicationDbContext context = database.CreateContext();

        Assert.Equal(51, await context.UserProfiles.CountAsync());
        Assert.Equal(36, await context.PatientProfiles.CountAsync());
        Assert.Equal(10, await context.ClinicianProfiles.CountAsync());
        Assert.Equal(4, await context.PharmacistProfiles.CountAsync());
        Assert.Equal(60, await context.AvailabilitySlots.CountAsync());
        Assert.Equal(36, await context.Appointments.CountAsync());
        Assert.Equal(14, await context.Consultations.CountAsync());
        Assert.Equal(12, await context.Medications.CountAsync());
        Assert.Equal(9, await context.Prescriptions.CountAsync());
        Assert.Equal(9, await context.Fulfillments.CountAsync());
        Assert.Equal(46, await context.AuditEvents.CountAsync());

        DateTimeOffset expectedAnchor = new(2030, 2, 15, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset markerTimestamp = await context.AuditEvents
            .Where(auditEvent => auditEvent.Action == "DemoDataSeeded")
            .Select(auditEvent => auditEvent.OccurredAtUtc)
            .SingleAsync();
        Assert.Equal(expectedAnchor, markerTimestamp);

        ProfileType[] expectedTypes =
        [
            ProfileType.Administrator,
            ProfileType.Doctor,
            ProfileType.Patient,
            ProfileType.Pharmacist,
        ];
        ProfileType[] configuredTypes = await context.UserProfiles
            .Where(profile => profile.Auth0Subject.StartsWith("test-auth|"))
            .OrderBy(profile => profile.ProfileType)
            .Select(profile => profile.ProfileType)
            .ToArrayAsync();
        Assert.Equal(expectedTypes, configuredTypes);

        Appointment completedAppointment = await context.Appointments
            .OrderBy(appointment => appointment.Id)
            .FirstAsync(appointment => appointment.Status == AppointmentStatus.Completed);
        AvailabilitySlot completedSlot = await context.AvailabilitySlots
            .SingleAsync(slot => slot.Id == completedAppointment.AvailabilitySlotId);
        ClinicianProfile completedClinician = await context.ClinicianProfiles
            .SingleAsync(profile => profile.Id == completedSlot.ClinicianProfileId);
        AuditEvent completionAudit = await context.AuditEvents.SingleAsync(
            auditEvent => auditEvent.Action == "AppointmentCompleted" &&
                auditEvent.AffectedEntityId == completedAppointment.Id);
        Assert.Equal(completedClinician.UserProfileId, completionAudit.ActorUserProfileId);
        Assert.Equal(completedSlot.EndsAtUtc, completionAudit.OccurredAtUtc);

        Prescription cancelledPrescription = await context.Prescriptions
            .SingleAsync(prescription => prescription.Status == PrescriptionStatus.Cancelled);
        ClinicianProfile prescriber = await context.ClinicianProfiles.SingleAsync(
            profile => profile.Id == cancelledPrescription.PrescriberClinicianProfileId);
        AuditEvent cancellationAudit = await context.AuditEvents.SingleAsync(
            auditEvent => auditEvent.Action == "PrescriptionCancelled" &&
                auditEvent.AffectedEntityId == cancelledPrescription.Id);
        Assert.Equal(prescriber.UserProfileId, cancellationAudit.ActorUserProfileId);
        Assert.Equal(cancelledPrescription.CancelledAtUtc, cancellationAudit.OccurredAtUtc);
    }
}
