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
using Microsoft.Extensions.DependencyInjection;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class MedicationPrescriptionEndpointsTests(AuthenticationDatabaseFixture database)
{
    [Fact]
    public async Task MedicationSearchRequiresDoctorAndMapsCatalogStatus()
    {
        MedicationCatalogItem catalogItem = new(
            901,
            "1049502",
            "Example medicine 10 MG Oral Tablet",
            "SCD",
            "10 mg",
            "Oral tablet",
            "RxNorm");
        StubMedicationCatalog catalog = new(ApplicationResult.Success(
            new MedicationCatalogSearch([catalogItem], MedicationCatalogStatus.Live)));
        using AuthenticationApiFactory factory = new(
            database.ConnectionString,
            medicationCatalog: catalog);
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage missingToken = await client.GetAsync(
            "/api/v1/medications?query=example");
        using HttpRequestMessage patientRequest = CreateAuthenticatedRequest(
            factory,
            HttpMethod.Get,
            "/api/v1/medications?query=example",
            AuthTestIdentities.PatientSubject,
            ApplicationRoles.Patient);
        HttpResponseMessage patientResponse = await client.SendAsync(patientRequest);
        using HttpRequestMessage doctorRequest = CreateDoctorRequest(
            factory,
            HttpMethod.Get,
            "/api/v1/medications?query=example&limit=5");
        HttpResponseMessage doctorResponse = await client.SendAsync(doctorRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patientResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, doctorResponse.StatusCode);
        MedicationCatalogSearchResponse body = await doctorResponse.Content
            .ReadFromJsonAsync<MedicationCatalogSearchResponse>()
            ?? throw new InvalidOperationException("The medication response was missing.");
        MedicationCatalogItemResponse item = Assert.Single(body.Items);
        Assert.Equal("Live", body.CatalogStatus);
        Assert.Equal(catalogItem.MedicationId, item.MedicationId);
        Assert.Equal("SCD", item.ConceptType);
        Assert.Equal("example", catalog.LastQuery);
        Assert.Equal(5, catalog.LastLimit);
    }

    [Fact]
    public async Task CatalogDependencyFailureReturnsProblemDetailsWithTraceId()
    {
        StubMedicationCatalog catalog = new(
            ApplicationResult.DependencyUnavailable<MedicationCatalogSearch>(
                "medication_catalog_unavailable",
                "No catalog source is available."));
        using AuthenticationApiFactory factory = new(
            database.ConnectionString,
            medicationCatalog: catalog);
        using HttpClient client = CreateClient(factory);
        using HttpRequestMessage request = CreateDoctorRequest(
            factory,
            HttpMethod.Get,
            "/api/v1/medications?query=missing");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "medication_catalog_unavailable",
            problem.RootElement.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task IssueCreatesServerDerivedPrescriptionFulfillmentAndAuditsAtomically()
    {
        ClinicalPrescriptionSetup setup = await CreateCompletedConsultationAsync(
            AuthTestIdentities.DoctorSubject);
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);
        using HttpRequestMessage request = CreateDoctorRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/consultations/{setup.ConsultationId}/prescriptions",
            new IssuePrescriptionRequest(
                setup.MedicationId,
                " 10 mg ",
                " Take once daily. ",
                30));

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        DoctorPrescriptionResponse body = await ReadPrescriptionAsync(response);
        Assert.Equal(
            $"/api/v1/prescriptions/{body.Id}",
            response.Headers.Location?.AbsolutePath);
        Assert.Equal("Issued", body.Status);
        Assert.Equal("Pending", body.FulfillmentStatus);
        Assert.Equal("10 mg", body.Dose);
        Assert.Equal("Take once daily.", body.Instructions);
        Assert.True(body.Version > 0);

        string responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("auth-test|", responseJson, StringComparison.OrdinalIgnoreCase);

        await using ApplicationDbContext context = database.CreateContext();
        Prescription prescription = await context.Prescriptions
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == body.Id);
        Fulfillment fulfillment = await context.Fulfillments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.PrescriptionId == body.Id);
        Assert.Equal(setup.PatientProfileId, prescription.PatientProfileId);
        Assert.Equal(setup.ClinicianProfileId, prescription.PrescriberClinicianProfileId);
        Assert.Equal(setup.RxCui, prescription.RxCuiSnapshot);
        Assert.Equal(setup.MedicationDisplayName, prescription.MedicationDisplayNameSnapshot);
        Assert.Equal(FulfillmentStatus.Pending, fulfillment.Status);
        Assert.Equal(
            2,
            await context.AuditEvents.CountAsync(auditEvent =>
                (auditEvent.Action == "PrescriptionIssued" &&
                    auditEvent.AffectedEntityId == prescription.Id) ||
                (auditEvent.Action == "FulfillmentCreated" &&
                    auditEvent.AffectedEntityId == fulfillment.Id)));
    }

    [Fact]
    public async Task CancellationUpdatesPrescriptionAndFulfillmentTogether()
    {
        ClinicalPrescriptionSetup setup = await CreateCompletedConsultationAsync(
            AuthTestIdentities.DoctorSubject);
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);
        DoctorPrescriptionResponse issued = await IssueAsync(client, factory, setup);
        using HttpRequestMessage cancellationRequest = CreateDoctorRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/prescriptions/{issued.Id}/cancellation",
            new CancelPrescriptionRequest(issued.Version));

        HttpResponseMessage response = await client.SendAsync(cancellationRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DoctorPrescriptionResponse cancelled = await ReadPrescriptionAsync(response);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Cancelled", cancelled.FulfillmentStatus);
        Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.True(cancelled.Version > issued.Version);

        await using ApplicationDbContext context = database.CreateContext();
        Prescription prescription = await context.Prescriptions
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == issued.Id);
        Fulfillment fulfillment = await context.Fulfillments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.PrescriptionId == issued.Id);
        Assert.Equal(PrescriptionStatus.Cancelled, prescription.Status);
        Assert.Equal(FulfillmentStatus.Cancelled, fulfillment.Status);
        Assert.Equal(prescription.CancelledAtUtc, fulfillment.CancelledAtUtc);
        Assert.Equal(
            2,
            await context.AuditEvents.CountAsync(auditEvent =>
                (auditEvent.Action == "PrescriptionCancelled" &&
                    auditEvent.AffectedEntityId == prescription.Id) ||
                (auditEvent.Action == "FulfillmentCancelled" &&
                    auditEvent.AffectedEntityId == fulfillment.Id)));
    }

    [Fact]
    public async Task OtherDoctorCannotDiscoverOrCancelPrescription()
    {
        ClinicalPrescriptionSetup setup = await CreateCompletedConsultationAsync(
            AuthTestIdentities.DoctorSubject);
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);
        DoctorPrescriptionResponse issued = await IssueAsync(client, factory, setup);
        using HttpRequestMessage getRequest = CreateAuthenticatedRequest(
            factory,
            HttpMethod.Get,
            $"/api/v1/prescriptions/{issued.Id}",
            AuthTestIdentities.OtherDoctorSubject,
            ApplicationRoles.Doctor);
        using HttpRequestMessage cancelRequest = CreateAuthenticatedRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/prescriptions/{issued.Id}/cancellation",
            AuthTestIdentities.OtherDoctorSubject,
            ApplicationRoles.Doctor,
            new CancelPrescriptionRequest(issued.Version));

        HttpResponseMessage getResponse = await client.SendAsync(getRequest);
        HttpResponseMessage cancelResponse = await client.SendAsync(cancelRequest);

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task StaleVersionAndDispensedFulfillmentBlockCancellation()
    {
        ClinicalPrescriptionSetup setup = await CreateCompletedConsultationAsync(
            AuthTestIdentities.DoctorSubject);
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);
        DoctorPrescriptionResponse issued = await IssueAsync(client, factory, setup);
        using HttpRequestMessage staleRequest = CreateDoctorRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/prescriptions/{issued.Id}/cancellation",
            new CancelPrescriptionRequest(issued.Version + 1));

        HttpResponseMessage staleResponse = await client.SendAsync(staleRequest);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        await using (ApplicationDbContext context = database.CreateContext())
        {
            long pharmacistProfileId = await context.PharmacistProfiles
                .Select(profile => profile.Id)
                .FirstAsync();
            Fulfillment fulfillment = await context.Fulfillments
                .SingleAsync(candidate => candidate.PrescriptionId == issued.Id);
            fulfillment.AssignedPharmacistProfileId = pharmacistProfileId;
            fulfillment.Status = FulfillmentStatus.Dispensed;
            fulfillment.ReviewStartedAtUtc = fulfillment.CreatedAtUtc.AddMinutes(1);
            fulfillment.ReadyAtUtc = fulfillment.CreatedAtUtc.AddMinutes(2);
            fulfillment.DispensedAtUtc = fulfillment.CreatedAtUtc.AddMinutes(3);
            await context.SaveChangesAsync();
        }

        using HttpRequestMessage dispensedRequest = CreateDoctorRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/prescriptions/{issued.Id}/cancellation",
            new CancelPrescriptionRequest(issued.Version));
        HttpResponseMessage dispensedResponse = await client.SendAsync(dispensedRequest);

        Assert.Equal(HttpStatusCode.Conflict, dispensedResponse.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(
            await dispensedResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            "prescription_already_dispensed",
            problem.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task IssuanceRollsBackWhenTheFinalAuditSaveFails()
    {
        ClinicalPrescriptionSetup setup = await CreateCompletedConsultationAsync(
            AuthTestIdentities.DoctorSubject);
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using IServiceScope scope = factory.Services.CreateScope();
        ApplicationDbContext concreteContext = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        ThrowOnThirdSaveDbContext failingContext = new(concreteContext);
        IApplicationTransaction transaction = scope.ServiceProvider
            .GetRequiredService<IApplicationTransaction>();
        IssuePrescriptionUseCase useCase = new(
            failingContext,
            transaction,
            new FixedTimeProvider(AuthTestClock.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(
            setup.DoctorUserProfileId,
            setup.ConsultationId,
            setup.MedicationId,
            "10 mg",
            "Take once daily.",
            30,
            "rollback-test"));

        await using ApplicationDbContext verificationContext = database.CreateContext();
        Assert.False(await verificationContext.Prescriptions
            .AnyAsync(prescription =>
                prescription.ConsultationId == setup.ConsultationId &&
                prescription.MedicationId == setup.MedicationId));
        Assert.False(await verificationContext.Fulfillments
            .AnyAsync(fulfillment =>
                fulfillment.Prescription.ConsultationId == setup.ConsultationId &&
                fulfillment.Prescription.MedicationId == setup.MedicationId));
    }

    private async Task<ClinicalPrescriptionSetup> CreateCompletedConsultationAsync(
        string doctorSubject)
    {
        await using ApplicationDbContext context = database.CreateContext();
        var clinician = await context.ClinicianProfiles
            .Where(profile => profile.UserProfile.Auth0Subject == doctorSubject)
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
        string unique = Guid.NewGuid().ToString("N");
        Medication medication = new()
        {
            RxCui = unique[..18],
            DisplayName = $"Transactional medicine {unique[..8]}",
            Classification = "SCD",
            Source = MedicationSource.RxNorm,
            LastVerifiedAtUtc = AuthTestClock.UtcNow,
            CreatedAtUtc = AuthTestClock.UtcNow,
        };
        context.Medications.Add(medication);

        DateTimeOffset start = AuthTestClock.UtcNow
            .AddDays(-2)
            .AddSeconds(Random.Shared.Next(1, 80_000));
        AvailabilitySlot slot = new()
        {
            ClinicianProfileId = clinician.Id,
            StartsAtUtc = start,
            EndsAtUtc = start.AddMinutes(45),
            CreatedAtUtc = start.AddDays(-1),
        };
        context.AvailabilitySlots.Add(slot);
        await context.SaveChangesAsync();

        Appointment appointment = new()
        {
            PatientProfileId = patientProfileId,
            AvailabilitySlotId = slot.Id,
            Reason = "Completed visit for prescription testing",
            Status = AppointmentStatus.Completed,
            CreatedAtUtc = start.AddDays(-1),
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        Consultation consultation = new()
        {
            AppointmentId = appointment.Id,
            Outcome = "Stable",
            ClinicalNotes = "Synthetic test note",
            PatientSummary = "Synthetic summary",
            CareInstructions = "Follow the plan",
            Status = ConsultationStatus.Completed,
            StartedAtUtc = start,
            CompletedAtUtc = start.AddMinutes(30),
            CreatedAtUtc = start,
        };
        context.Consultations.Add(consultation);
        await context.SaveChangesAsync();

        return new ClinicalPrescriptionSetup(
            consultation.Id,
            medication.Id,
            medication.RxCui,
            medication.DisplayName,
            patientProfileId,
            clinician.Id,
            clinician.UserProfileId);
    }

    private static async Task<DoctorPrescriptionResponse> IssueAsync(
        HttpClient client,
        AuthenticationApiFactory factory,
        ClinicalPrescriptionSetup setup)
    {
        using HttpRequestMessage request = CreateDoctorRequest(
            factory,
            HttpMethod.Post,
            $"/api/v1/consultations/{setup.ConsultationId}/prescriptions",
            new IssuePrescriptionRequest(
                setup.MedicationId,
                "10 mg",
                "Take once daily.",
                30));
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadPrescriptionAsync(response);
    }

    private static HttpClient CreateClient(AuthenticationApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static HttpRequestMessage CreateDoctorRequest(
        AuthenticationApiFactory factory,
        HttpMethod method,
        string path,
        object? body = null) =>
        CreateAuthenticatedRequest(
            factory,
            method,
            path,
            AuthTestIdentities.DoctorSubject,
            ApplicationRoles.Doctor,
            body);

    private static HttpRequestMessage CreateAuthenticatedRequest(
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

    private static async Task<DoctorPrescriptionResponse> ReadPrescriptionAsync(
        HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<DoctorPrescriptionResponse>()
        ?? throw new InvalidOperationException("The prescription response was missing.");

    private sealed record ClinicalPrescriptionSetup(
        long ConsultationId,
        long MedicationId,
        string RxCui,
        string MedicationDisplayName,
        long PatientProfileId,
        long ClinicianProfileId,
        long DoctorUserProfileId);

    private sealed class StubMedicationCatalog(
        ApplicationResult<MedicationCatalogSearch> result) : IMedicationCatalog
    {
        public string? LastQuery { get; private set; }

        public int LastLimit { get; private set; }

        public Task<ApplicationResult<MedicationCatalogSearch>> SearchAsync(
            string normalizedQuery,
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastQuery = normalizedQuery;
            LastLimit = limit;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowOnThirdSaveDbContext(ApplicationDbContext inner)
        : IApplicationDbContext
    {
        private int saveCount;

        public DbSet<UserProfile> UserProfiles => inner.UserProfiles;

        public DbSet<PatientProfile> PatientProfiles => inner.PatientProfiles;

        public DbSet<ClinicianProfile> ClinicianProfiles => inner.ClinicianProfiles;

        public DbSet<PharmacistProfile> PharmacistProfiles => inner.PharmacistProfiles;

        public DbSet<AvailabilitySlot> AvailabilitySlots => inner.AvailabilitySlots;

        public DbSet<Appointment> Appointments => inner.Appointments;

        public DbSet<Consultation> Consultations => inner.Consultations;

        public DbSet<Medication> Medications => inner.Medications;

        public DbSet<Prescription> Prescriptions => inner.Prescriptions;

        public DbSet<Fulfillment> Fulfillments => inner.Fulfillments;

        public DbSet<AuditEvent> AuditEvents => inner.AuditEvents;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            saveCount++;
            return saveCount == 3
                ? Task.FromException<int>(
                    new InvalidOperationException("Synthetic audit save failure."))
                : inner.SaveChangesAsync(cancellationToken);
        }
    }
}
