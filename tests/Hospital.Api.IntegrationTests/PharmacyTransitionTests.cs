using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Audit;
using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Persistence;
using Hospital.Core.Pharmacy;
using Hospital.Core.Prescriptions;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class PharmacyTransitionTests(AuthenticationDatabaseFixture database)
{
    [Fact]
    public async Task PharmacistClaimsMarksReadyAndDispensesWithAtomicAudits()
    {
        TransitionSetup setup = await CreateFulfillmentAsync();
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);
        using HttpRequestMessage doctorRequest = CreateRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/fulfillments/{setup.FulfillmentId}/transitions",
            AuthTestIdentities.DoctorSubject,
            ApplicationRoles.Doctor,
            new TransitionFulfillmentRequest("InReview", setup.FulfillmentVersion));

        HttpResponseMessage doctorResponse = await client.SendAsync(doctorRequest);
        Assert.Equal(HttpStatusCode.Forbidden, doctorResponse.StatusCode);

        PharmacyFulfillmentResponse inReview = await TransitionAsync(
            client,
            factory,
            setup.FulfillmentId,
            "InReview",
            setup.FulfillmentVersion);
        Assert.Equal("InReview", inReview.Status);
        Assert.True(inReview.AssignedToCurrentPharmacist);
        Assert.Equal(AuthTestClock.UtcNow, inReview.ReviewStartedAtUtc);
        Assert.True(inReview.Version > setup.FulfillmentVersion);

        PharmacyFulfillmentResponse ready = await TransitionAsync(
            client,
            factory,
            setup.FulfillmentId,
            "Ready",
            inReview.Version);
        Assert.Equal("Ready", ready.Status);
        Assert.Equal(AuthTestClock.UtcNow, ready.ReadyAtUtc);
        Assert.True(ready.Version > inReview.Version);

        PharmacyFulfillmentResponse dispensed = await TransitionAsync(
            client,
            factory,
            setup.FulfillmentId,
            "Dispensed",
            ready.Version);
        Assert.Equal("Dispensed", dispensed.Status);
        Assert.Equal(AuthTestClock.UtcNow, dispensed.DispensedAtUtc);
        Assert.True(dispensed.Version > ready.Version);

        await using ApplicationDbContext context = database.CreateContext();
        Fulfillment persisted = await context.Fulfillments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == setup.FulfillmentId);
        Assert.Equal(FulfillmentStatus.Dispensed, persisted.Status);
        Assert.Equal(setup.PharmacistProfileId, persisted.AssignedPharmacistProfileId);

        string[] expectedActions =
        [
            "FulfillmentReviewStarted",
            "FulfillmentMarkedReady",
            "FulfillmentDispensed",
        ];
        AuditEvent[] audits = await context.AuditEvents
            .AsNoTracking()
            .Where(audit =>
                audit.AffectedEntityType == nameof(Fulfillment) &&
                audit.AffectedEntityId == setup.FulfillmentId &&
                expectedActions.Contains(audit.Action))
            .OrderBy(audit => audit.Id)
            .ToArrayAsync();
        Assert.Equal(expectedActions, audits.Select(audit => audit.Action));
        Assert.All(audits, audit =>
        {
            Assert.Equal(setup.PharmacistUserProfileId, audit.ActorUserProfileId);
            Assert.False(string.IsNullOrWhiteSpace(audit.TraceId));
            Assert.Contains("\"from\"", audit.MetadataJson, StringComparison.Ordinal);
            Assert.Contains("\"to\"", audit.MetadataJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AssignedElsewhereIsConcealedAndCannotBeAdvanced()
    {
        AdditionalPharmacist other = await CreateAdditionalPharmacistAsync();
        TransitionSetup setup = await CreateFulfillmentAsync(
            FulfillmentStatus.InReview,
            other.Subject);
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);
        using HttpRequestMessage request = CreatePharmacistRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/fulfillments/{setup.FulfillmentId}/transitions",
            new TransitionFulfillmentRequest("Ready", setup.FulfillmentVersion));

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("fulfillment_not_found", await ReadErrorCodeAsync(response));
        await using ApplicationDbContext context = database.CreateContext();
        Fulfillment persisted = await context.Fulfillments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == setup.FulfillmentId);
        Assert.Equal(FulfillmentStatus.InReview, persisted.Status);
        Assert.Equal(other.ProfileId, persisted.AssignedPharmacistProfileId);
        Assert.False(await context.AuditEvents.AnyAsync(audit =>
            audit.AffectedEntityType == nameof(Fulfillment) &&
            audit.AffectedEntityId == setup.FulfillmentId &&
            audit.Action == "FulfillmentMarkedReady"));
    }

    [Fact]
    public async Task StaleForwardSkippingAndTerminalTransitionsAreRejected()
    {
        TransitionSetup pending = await CreateFulfillmentAsync();
        TransitionSetup dispensed = await CreateFulfillmentAsync(
            FulfillmentStatus.Dispensed,
            AuthTestIdentities.PharmacistSubject);
        TransitionSetup cancelled = await CreateFulfillmentAsync(
            FulfillmentStatus.Cancelled,
            AuthTestIdentities.PharmacistSubject);
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage stale = await SendTransitionAsync(
            client,
            factory,
            pending.FulfillmentId,
            "InReview",
            pending.FulfillmentVersion + 1);
        HttpResponseMessage skipped = await SendTransitionAsync(
            client,
            factory,
            pending.FulfillmentId,
            "Ready",
            pending.FulfillmentVersion);
        HttpResponseMessage invalidTarget = await SendTransitionAsync(
            client,
            factory,
            pending.FulfillmentId,
            "Cancelled",
            pending.FulfillmentVersion);
        HttpResponseMessage afterDispense = await SendTransitionAsync(
            client,
            factory,
            dispensed.FulfillmentId,
            "Dispensed",
            dispensed.FulfillmentVersion);
        HttpResponseMessage afterCancellation = await SendTransitionAsync(
            client,
            factory,
            cancelled.FulfillmentId,
            "InReview",
            cancelled.FulfillmentVersion);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("fulfillment_changed", await ReadErrorCodeAsync(stale));
        Assert.Equal(HttpStatusCode.Conflict, skipped.StatusCode);
        Assert.Equal("fulfillment_transition_invalid", await ReadErrorCodeAsync(skipped));
        Assert.Equal(HttpStatusCode.BadRequest, invalidTarget.StatusCode);
        Assert.Equal(
            "invalid_fulfillment_transition_target",
            await ReadErrorCodeAsync(invalidTarget));
        Assert.Equal(HttpStatusCode.Conflict, afterDispense.StatusCode);
        Assert.Equal(
            "fulfillment_transition_invalid",
            await ReadErrorCodeAsync(afterDispense));
        Assert.Equal(HttpStatusCode.Conflict, afterCancellation.StatusCode);
        Assert.Equal(
            "fulfillment_transition_invalid",
            await ReadErrorCodeAsync(afterCancellation));
    }

    [Fact]
    public async Task ConcurrentClaimsAllowExactlyOnePharmacistToWin()
    {
        TransitionSetup setup = await CreateFulfillmentAsync();
        AdditionalPharmacist other = await CreateAdditionalPharmacistAsync();
        AsyncSaveBarrier barrier = new(participantCount: 2);
        await using ApplicationDbContext firstContext = database.CreateContext();
        await using ApplicationDbContext secondContext = database.CreateContext();
        TransitionFulfillmentUseCase first = new(
            new CoordinatedSaveDbContext(firstContext, barrier),
            new FixedTimeProvider(AuthTestClock.UtcNow));
        TransitionFulfillmentUseCase second = new(
            new CoordinatedSaveDbContext(secondContext, barrier),
            new FixedTimeProvider(AuthTestClock.UtcNow));

        Task<ApplicationResult<PharmacyFulfillmentDetails>> firstClaim = first.ExecuteAsync(
            setup.PharmacistUserProfileId,
            setup.FulfillmentId,
            FulfillmentStatus.InReview,
            setup.FulfillmentVersion,
            "first-claim");
        Task<ApplicationResult<PharmacyFulfillmentDetails>> secondClaim = second.ExecuteAsync(
            other.UserProfileId,
            setup.FulfillmentId,
            FulfillmentStatus.InReview,
            setup.FulfillmentVersion,
            "second-claim");

        ApplicationResult<PharmacyFulfillmentDetails>[] results = await Task.WhenAll(
            firstClaim,
            secondClaim);

        ApplicationResult<PharmacyFulfillmentDetails> winner = Assert.Single(
            results,
            result => result.IsSuccess);
        ApplicationResult<PharmacyFulfillmentDetails> loser = Assert.Single(
            results,
            result => !result.IsSuccess);
        Assert.Equal(ApplicationFailure.Conflict, loser.Failure);
        Assert.Equal("fulfillment_changed", loser.ErrorCode);
        Assert.True(winner.Value!.AssignedToCurrentPharmacist);

        await using ApplicationDbContext verification = database.CreateContext();
        Fulfillment persisted = await verification.Fulfillments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == setup.FulfillmentId);
        Assert.Equal(FulfillmentStatus.InReview, persisted.Status);
        Assert.Contains(
            persisted.AssignedPharmacistProfileId,
            new long?[] { setup.PharmacistProfileId, other.ProfileId });
        Assert.Equal(
            1,
            await verification.AuditEvents.CountAsync(audit =>
                audit.AffectedEntityType == nameof(Fulfillment) &&
                audit.AffectedEntityId == setup.FulfillmentId &&
                audit.Action == "FulfillmentReviewStarted"));
    }

    [Fact]
    public async Task ClaimAndPrescriptionCancellationRaceRemainConsistent()
    {
        TransitionSetup setup = await CreateFulfillmentAsync();
        AsyncSaveBarrier barrier = new(participantCount: 2);
        await using ApplicationDbContext transitionContext = database.CreateContext();
        await using ApplicationDbContext cancellationContext = database.CreateContext();
        TransitionFulfillmentUseCase transition = new(
            new CoordinatedSaveDbContext(transitionContext, barrier),
            new FixedTimeProvider(AuthTestClock.UtcNow));
        CancelPrescriptionUseCase cancellation = new(
            new CoordinatedSaveDbContext(cancellationContext, barrier),
            new FixedTimeProvider(AuthTestClock.UtcNow));

        Task<ApplicationResult<PharmacyFulfillmentDetails>> transitionTask = transition.ExecuteAsync(
            setup.PharmacistUserProfileId,
            setup.FulfillmentId,
            FulfillmentStatus.InReview,
            setup.FulfillmentVersion,
            "claim-race");
        Task<ApplicationResult<DoctorPrescriptionDetails>> cancellationTask = cancellation.ExecuteAsync(
            setup.DoctorUserProfileId,
            setup.PrescriptionId,
            setup.PrescriptionVersion,
            "cancellation-race");

        await Task.WhenAll((Task)transitionTask, cancellationTask);
        ApplicationResult<PharmacyFulfillmentDetails> transitionResult = await transitionTask;
        ApplicationResult<DoctorPrescriptionDetails> cancellationResult = await cancellationTask;
        Assert.NotEqual(transitionResult.IsSuccess, cancellationResult.IsSuccess);
        ApplicationResult<PharmacyFulfillmentDetails>? transitionFailure = transitionResult.IsSuccess
            ? null
            : transitionResult;
        ApplicationResult<DoctorPrescriptionDetails>? cancellationFailure =
            cancellationResult.IsSuccess ? null : cancellationResult;
        Assert.True(
            transitionFailure?.Failure == ApplicationFailure.Conflict ||
            cancellationFailure?.Failure == ApplicationFailure.Conflict);

        await using ApplicationDbContext verification = database.CreateContext();
        Prescription prescription = await verification.Prescriptions
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == setup.PrescriptionId);
        Fulfillment fulfillment = await verification.Fulfillments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == setup.FulfillmentId);

        if (transitionResult.IsSuccess)
        {
            Assert.Equal(PrescriptionStatus.Issued, prescription.Status);
            Assert.Equal(FulfillmentStatus.InReview, fulfillment.Status);
            Assert.Equal(setup.PharmacistProfileId, fulfillment.AssignedPharmacistProfileId);
            Assert.Equal(
                1,
                await CountTransitionAuditsAsync(verification, setup.FulfillmentId));
            Assert.Equal(
                0,
                await CountCancellationAuditsAsync(
                    verification,
                    setup.PrescriptionId,
                    setup.FulfillmentId));
        }
        else
        {
            Assert.Equal(PrescriptionStatus.Cancelled, prescription.Status);
            Assert.Equal(FulfillmentStatus.Cancelled, fulfillment.Status);
            Assert.Null(fulfillment.AssignedPharmacistProfileId);
            Assert.Equal(
                0,
                await CountTransitionAuditsAsync(verification, setup.FulfillmentId));
            Assert.Equal(
                2,
                await CountCancellationAuditsAsync(
                    verification,
                    setup.PrescriptionId,
                    setup.FulfillmentId));
        }
    }

    [Fact]
    public async Task FulfillmentUpdateRollsBackWhenAuditInsertFails()
    {
        TransitionSetup setup = await CreateFulfillmentAsync();
        await using ApplicationDbContext context = database.CreateContext();
        TransitionFulfillmentUseCase useCase = new(
            new InvalidAuditOnSaveDbContext(context),
            new FixedTimeProvider(AuthTestClock.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => useCase.ExecuteAsync(
            setup.PharmacistUserProfileId,
            setup.FulfillmentId,
            FulfillmentStatus.InReview,
            setup.FulfillmentVersion,
            "audit-failure"));

        await using ApplicationDbContext verification = database.CreateContext();
        Fulfillment persisted = await verification.Fulfillments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == setup.FulfillmentId);
        Assert.Equal(FulfillmentStatus.Pending, persisted.Status);
        Assert.Null(persisted.AssignedPharmacistProfileId);
        Assert.Null(persisted.ReviewStartedAtUtc);
        Assert.Equal(setup.FulfillmentVersion, persisted.Version);
        Assert.False(await verification.AuditEvents.AnyAsync(audit =>
            audit.AffectedEntityType == nameof(Fulfillment) &&
            audit.AffectedEntityId == setup.FulfillmentId &&
            audit.Action == "FulfillmentReviewStarted"));
    }

    private async Task<TransitionSetup> CreateFulfillmentAsync(
        FulfillmentStatus status = FulfillmentStatus.Pending,
        string? assignedPharmacistSubject = null)
    {
        await using ApplicationDbContext context = database.CreateContext();
        var clinician = await context.ClinicianProfiles
            .Where(profile =>
                profile.UserProfile.Auth0Subject == AuthTestIdentities.DoctorSubject)
            .Select(profile => new
            {
                profile.Id,
                profile.UserProfileId,
            })
            .SingleAsync();
        long patientProfileId = await context.PatientProfiles
            .Where(profile =>
                profile.UserProfile.Auth0Subject == AuthTestIdentities.PatientSubject)
            .Select(profile => profile.Id)
            .SingleAsync();
        var currentPharmacist = await context.PharmacistProfiles
            .Where(profile =>
                profile.UserProfile.Auth0Subject == AuthTestIdentities.PharmacistSubject)
            .Select(profile => new
            {
                profile.Id,
                profile.UserProfileId,
            })
            .SingleAsync();
        long? assignedPharmacistProfileId = assignedPharmacistSubject is null
            ? null
            : await context.PharmacistProfiles
                .Where(profile => profile.UserProfile.Auth0Subject == assignedPharmacistSubject)
                .Select(profile => (long?)profile.Id)
                .SingleAsync();

        string unique = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = AuthTestClock.UtcNow
            .AddDays(-3)
            .AddSeconds(Random.Shared.Next(1, 80_000));
        Medication medication = new()
        {
            RxCui = unique[..18],
            DisplayName = $"Transition medicine {unique[..8]}",
            Classification = "SCD",
            Strength = "10 mg",
            DoseForm = "Oral tablet",
            Source = MedicationSource.RxNorm,
            LastVerifiedAtUtc = startedAt,
            CreatedAtUtc = startedAt,
        };
        context.Medications.Add(medication);

        AvailabilitySlot slot = new()
        {
            ClinicianProfileId = clinician.Id,
            StartsAtUtc = startedAt,
            EndsAtUtc = startedAt.AddMinutes(45),
            CreatedAtUtc = startedAt.AddDays(-1),
        };
        context.AvailabilitySlots.Add(slot);
        await context.SaveChangesAsync();

        Appointment appointment = new()
        {
            PatientProfileId = patientProfileId,
            AvailabilitySlotId = slot.Id,
            Reason = "Synthetic pharmacy transition",
            Status = AppointmentStatus.Completed,
            CreatedAtUtc = startedAt.AddDays(-1),
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        Consultation consultation = new()
        {
            AppointmentId = appointment.Id,
            Outcome = "Stable",
            ClinicalNotes = "Synthetic transition note",
            PatientSummary = "Synthetic transition summary",
            CareInstructions = "Follow the synthetic plan",
            Status = ConsultationStatus.Completed,
            StartedAtUtc = startedAt,
            CompletedAtUtc = startedAt.AddMinutes(30),
            CreatedAtUtc = startedAt,
        };
        context.Consultations.Add(consultation);
        await context.SaveChangesAsync();

        DateTimeOffset issuedAt = consultation.CompletedAtUtc!.Value.AddMinutes(1);
        DateTimeOffset createdAt = issuedAt.AddMinutes(1);
        bool isCancelled = status == FulfillmentStatus.Cancelled;
        Prescription prescription = new()
        {
            ConsultationId = consultation.Id,
            MedicationId = medication.Id,
            PrescriberClinicianProfileId = clinician.Id,
            PatientProfileId = patientProfileId,
            RxCuiSnapshot = medication.RxCui,
            MedicationDisplayNameSnapshot = medication.DisplayName,
            Dose = "10 mg",
            Instructions = "Take once daily.",
            Quantity = 30,
            Status = isCancelled ? PrescriptionStatus.Cancelled : PrescriptionStatus.Issued,
            IssuedAtUtc = issuedAt,
            CancelledAtUtc = isCancelled ? createdAt.AddMinutes(4) : null,
        };
        context.Prescriptions.Add(prescription);
        await context.SaveChangesAsync();

        bool hasReview = status != FulfillmentStatus.Pending;
        Fulfillment fulfillment = new()
        {
            PrescriptionId = prescription.Id,
            AssignedPharmacistProfileId = hasReview ? assignedPharmacistProfileId : null,
            Status = status,
            CreatedAtUtc = createdAt,
            ReviewStartedAtUtc = hasReview ? createdAt.AddMinutes(1) : null,
            ReadyAtUtc = status is FulfillmentStatus.Ready or FulfillmentStatus.Dispensed
                ? createdAt.AddMinutes(2)
                : null,
            DispensedAtUtc = status == FulfillmentStatus.Dispensed
                ? createdAt.AddMinutes(3)
                : null,
            CancelledAtUtc = isCancelled ? createdAt.AddMinutes(4) : null,
        };
        context.Fulfillments.Add(fulfillment);
        await context.SaveChangesAsync();

        return new TransitionSetup(
            fulfillment.Id,
            fulfillment.Version,
            prescription.Id,
            prescription.Version,
            clinician.UserProfileId,
            currentPharmacist.Id,
            currentPharmacist.UserProfileId);
    }

    private async Task<AdditionalPharmacist> CreateAdditionalPharmacistAsync()
    {
        string unique = Guid.NewGuid().ToString("N");
        string subject = $"auth-test|pharmacist-{unique}";
        await using ApplicationDbContext context = database.CreateContext();
        UserProfile userProfile = new()
        {
            Auth0Subject = subject,
            DisplayName = $"Pharmacist {unique[..8]}",
            ProfileType = ProfileType.Pharmacist,
            Status = AccountStatus.Active,
            CreatedAtUtc = AuthTestClock.UtcNow,
        };
        context.UserProfiles.Add(userProfile);
        await context.SaveChangesAsync();

        PharmacistProfile profile = new()
        {
            UserProfileId = userProfile.Id,
            StaffIdentifier = $"PHR-{unique[..12]}",
            PharmacyName = "Test Pharmacy",
            CreatedAtUtc = AuthTestClock.UtcNow,
        };
        context.PharmacistProfiles.Add(profile);
        await context.SaveChangesAsync();
        return new AdditionalPharmacist(subject, userProfile.Id, profile.Id);
    }

    private static async Task<PharmacyFulfillmentResponse> TransitionAsync(
        HttpClient client,
        AuthenticationApiFactory factory,
        long fulfillmentId,
        string targetStatus,
        uint expectedVersion)
    {
        HttpResponseMessage response = await SendTransitionAsync(
            client,
            factory,
            fulfillmentId,
            targetStatus,
            expectedVersion);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<PharmacyFulfillmentResponse>()
            ?? throw new InvalidOperationException("The fulfillment response was missing.");
    }

    private static async Task<HttpResponseMessage> SendTransitionAsync(
        HttpClient client,
        AuthenticationApiFactory factory,
        long fulfillmentId,
        string targetStatus,
        uint expectedVersion)
    {
        using HttpRequestMessage request = CreatePharmacistRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/fulfillments/{fulfillmentId}/transitions",
            new TransitionFulfillmentRequest(targetStatus, expectedVersion));
        return await client.SendAsync(request);
    }

    private static HttpClient CreateClient(AuthenticationApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static HttpRequestMessage CreatePharmacistRequest(
        AuthenticationApiFactory factory,
        HttpMethod method,
        string path,
        object? body = null) =>
        CreateRequest(
            factory,
            method,
            path,
            AuthTestIdentities.PharmacistSubject,
            ApplicationRoles.Pharmacist,
            body);

    private static HttpRequestMessage CreateRequest(
        AuthenticationApiFactory factory,
        HttpMethod method,
        string path,
        string subject,
        string role,
        object? body = null)
    {
        string token = factory.CreateToken(
        [
            new Claim("sub", subject),
            new Claim(AuthenticationApiFactory.RoleClaim, role),
        ]);
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errorCode").GetString();
    }

    private static Task<int> CountTransitionAuditsAsync(
        ApplicationDbContext context,
        long fulfillmentId) =>
        context.AuditEvents.CountAsync(audit =>
            audit.AffectedEntityType == nameof(Fulfillment) &&
            audit.AffectedEntityId == fulfillmentId &&
            audit.Action == "FulfillmentReviewStarted");

    private static Task<int> CountCancellationAuditsAsync(
        ApplicationDbContext context,
        long prescriptionId,
        long fulfillmentId) =>
        context.AuditEvents.CountAsync(audit =>
            (audit.Action == "PrescriptionCancelled" &&
                audit.AffectedEntityId == prescriptionId) ||
            (audit.Action == "FulfillmentCancelled" &&
                audit.AffectedEntityId == fulfillmentId));

    private sealed record TransitionSetup(
        long FulfillmentId,
        uint FulfillmentVersion,
        long PrescriptionId,
        uint PrescriptionVersion,
        long DoctorUserProfileId,
        long PharmacistProfileId,
        long PharmacistUserProfileId);

    private sealed record AdditionalPharmacist(
        string Subject,
        long UserProfileId,
        long ProfileId);

    private sealed class AsyncSaveBarrier(int participantCount)
    {
        private readonly TaskCompletionSource released = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivalCount;

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivalCount) == participantCount)
            {
                released.TrySetResult();
            }

            await released.Task.WaitAsync(cancellationToken);
        }
    }

    private abstract class DelegatingDbContext(ApplicationDbContext inner)
        : IApplicationDbContext
    {
        protected ApplicationDbContext Inner { get; } = inner;

        public DbSet<UserProfile> UserProfiles => Inner.UserProfiles;

        public DbSet<PatientProfile> PatientProfiles => Inner.PatientProfiles;

        public DbSet<ClinicianProfile> ClinicianProfiles => Inner.ClinicianProfiles;

        public DbSet<PharmacistProfile> PharmacistProfiles => Inner.PharmacistProfiles;

        public DbSet<AvailabilitySlot> AvailabilitySlots => Inner.AvailabilitySlots;

        public DbSet<Appointment> Appointments => Inner.Appointments;

        public DbSet<Consultation> Consultations => Inner.Consultations;

        public DbSet<Medication> Medications => Inner.Medications;

        public DbSet<Prescription> Prescriptions => Inner.Prescriptions;

        public DbSet<Fulfillment> Fulfillments => Inner.Fulfillments;

        public DbSet<AuditEvent> AuditEvents => Inner.AuditEvents;

        public virtual Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            Inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class CoordinatedSaveDbContext(
        ApplicationDbContext inner,
        AsyncSaveBarrier barrier) : DelegatingDbContext(inner)
    {
        public override async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            await barrier.ArriveAsync(cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class InvalidAuditOnSaveDbContext(ApplicationDbContext inner)
        : DelegatingDbContext(inner)
    {
        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            Inner.AuditEvents.Add(new AuditEvent
            {
                Action = new string('x', 101),
                AffectedEntityType = nameof(Fulfillment),
                OccurredAtUtc = AuthTestClock.UtcNow,
            });
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
