using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Api.IntegrationTests;

[Collection(PostgreSqlDatabaseTestGroup.Name)]
public sealed class SchedulingConcurrencyTests(PostgreSqlDatabaseFixture database)
{
    [Fact]
    public async Task TwoBookingsForOneSlotProduceOneAppointment()
    {
        await database.EnsureMigratedAsync();
        (long userProfileId, AvailabilitySlot slot) = await SeedBookingScenarioAsync();

        await using ApplicationDbContext firstContext = database.CreateContext();
        await using ApplicationDbContext secondContext = database.CreateContext();
        BookAppointmentUseCase first = new(firstContext, TimeProvider.System);
        BookAppointmentUseCase second = new(secondContext, TimeProvider.System);

        SchedulingResult<AppointmentSummary>[] results = await Task.WhenAll(
            first.ExecuteAsync(userProfileId, slot.Id, slot.Version, "First request"),
            second.ExecuteAsync(userProfileId, slot.Id, slot.Version, "Second request"));

        Assert.Single(results, result => result.IsSuccess);
        SchedulingResult<AppointmentSummary> conflict = Assert.Single(
            results,
            result => result.Failure == SchedulingFailure.Conflict);
        Assert.Equal("availability_taken", conflict.ErrorCode);

        await using ApplicationDbContext verificationContext = database.CreateContext();
        int activeCount = await verificationContext.Appointments.CountAsync(
            appointment =>
                appointment.AvailabilitySlotId == slot.Id &&
                appointment.Status != AppointmentStatus.Cancelled);
        Assert.Equal(1, activeCount);
    }

    [Fact]
    public async Task TwoCancellationsProduceOneCancellationAndOneConflict()
    {
        await database.EnsureMigratedAsync();
        (long userProfileId, AvailabilitySlot slot) = await SeedBookingScenarioAsync();
        await using (ApplicationDbContext seedContext = database.CreateContext())
        {
            long patientProfileId = await seedContext.PatientProfiles
                .Where(profile => profile.UserProfileId == userProfileId)
                .Select(profile => profile.Id)
                .SingleAsync();
            seedContext.Appointments.Add(new Appointment
            {
                PatientProfileId = patientProfileId,
                AvailabilitySlotId = slot.Id,
                Reason = "Concurrent cancellation",
                Status = AppointmentStatus.Scheduled,
                CreatedAtUtc = AuthTestClock.UtcNow.AddDays(-1),
            });
            await seedContext.SaveChangesAsync();
        }

        long appointmentId;
        uint appointmentVersion;
        await using (ApplicationDbContext readContext = database.CreateContext())
        {
            var appointment = await readContext.Appointments
                .AsNoTracking()
                .Where(candidate => candidate.AvailabilitySlotId == slot.Id)
                .Select(candidate => new { candidate.Id, candidate.Version })
                .SingleAsync();
            appointmentId = appointment.Id;
            appointmentVersion = appointment.Version;
        }

        TimeProvider clock = new FixedTimeProvider(AuthTestClock.UtcNow);
        await using ApplicationDbContext firstContext = database.CreateContext();
        await using ApplicationDbContext secondContext = database.CreateContext();
        CancelPatientAppointmentUseCase first = new(firstContext, clock);
        CancelPatientAppointmentUseCase second = new(secondContext, clock);

        SchedulingResult<AppointmentSummary>[] results = await Task.WhenAll(
            first.ExecuteAsync(
                userProfileId,
                appointmentId,
                appointmentVersion,
                nameof(AppointmentStatus.Cancelled),
                "First request"),
            second.ExecuteAsync(
                userProfileId,
                appointmentId,
                appointmentVersion,
                nameof(AppointmentStatus.Cancelled),
                "Second request"));

        Assert.Single(results, result => result.IsSuccess);
        SchedulingResult<AppointmentSummary> conflict = Assert.Single(
            results,
            result => result.Failure == SchedulingFailure.Conflict);
        Assert.True(
            conflict.ErrorCode is "appointment_changed" or "appointment_not_cancellable");

        await using ApplicationDbContext verificationContext = database.CreateContext();
        Appointment cancelled = await verificationContext.Appointments
            .SingleAsync(candidate => candidate.Id == appointmentId);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CancelledAtUtc);
    }

    private async Task<(long UserProfileId, AvailabilitySlot Slot)> SeedBookingScenarioAsync()
    {
        await using ApplicationDbContext context = database.CreateContext();
        DateTimeOffset createdAtUtc = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        UserProfile patientUser = new()
        {
            Auth0Subject = $"concurrency-test|patient-{Guid.NewGuid():N}",
            DisplayName = "Concurrency Patient",
            ProfileType = ProfileType.Patient,
            Status = AccountStatus.Active,
            CreatedAtUtc = createdAtUtc,
        };
        UserProfile doctorUser = new()
        {
            Auth0Subject = $"concurrency-test|doctor-{Guid.NewGuid():N}",
            DisplayName = "Concurrency Doctor",
            ProfileType = ProfileType.Doctor,
            Status = AccountStatus.Active,
            CreatedAtUtc = createdAtUtc,
        };
        context.UserProfiles.AddRange(patientUser, doctorUser);
        await context.SaveChangesAsync();

        PatientProfile patient = new()
        {
            UserProfileId = patientUser.Id,
            MedicalRecordNumber = $"CON-{Guid.NewGuid():N}"[..20],
            DateOfBirth = new DateOnly(1990, 1, 1),
            CreatedAtUtc = createdAtUtc,
        };
        ClinicianProfile clinician = new()
        {
            UserProfileId = doctorUser.Id,
            StaffIdentifier = $"CON-{Guid.NewGuid():N}"[..20],
            Specialty = "Concurrency Medicine",
            CreatedAtUtc = createdAtUtc,
        };
        context.PatientProfiles.Add(patient);
        context.ClinicianProfiles.Add(clinician);
        await context.SaveChangesAsync();

        AvailabilitySlot slot = new()
        {
            ClinicianProfileId = clinician.Id,
            StartsAtUtc = new DateTimeOffset(2035, 1, 15, 17, 0, 0, TimeSpan.Zero),
            EndsAtUtc = new DateTimeOffset(2035, 1, 15, 17, 45, 0, TimeSpan.Zero),
            CreatedAtUtc = createdAtUtc,
        };
        context.AvailabilitySlots.Add(slot);
        await context.SaveChangesAsync();
        return (patientUser.Id, slot);
    }
}
