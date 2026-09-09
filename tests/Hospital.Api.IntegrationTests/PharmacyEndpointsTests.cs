using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Pharmacy;
using Hospital.Core.Prescriptions;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class PharmacyEndpointsTests : IDisposable
{
    private readonly AuthenticationDatabaseFixture database;
    private readonly AuthenticationApiFactory factory;
    private readonly HttpClient client;

    public PharmacyEndpointsTests(AuthenticationDatabaseFixture database)
    {
        this.database = database;
        factory = new AuthenticationApiFactory(database.ConnectionString);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task FulfillmentEndpointsRequireAnAuthorizedPharmacist()
    {
        HttpResponseMessage missingToken = await client.GetAsync("/api/v1/fulfillments");
        using HttpRequestMessage doctorRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/v1/fulfillments",
            AuthTestIdentities.DoctorSubject,
            ApplicationRoles.Doctor);
        HttpResponseMessage doctorResponse = await client.SendAsync(doctorRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, doctorResponse.StatusCode);
    }

    [Fact]
    public async Task DefaultQueueContainsSharedPendingAndOnlyCurrentPharmacistActiveWork()
    {
        PharmacySetup pending = await CreateFulfillmentAsync(FulfillmentStatus.Pending);
        PharmacySetup inReview = await CreateFulfillmentAsync(
            FulfillmentStatus.InReview,
            assignedToCurrentPharmacist: true);
        PharmacySetup ready = await CreateFulfillmentAsync(
            FulfillmentStatus.Ready,
            assignedToCurrentPharmacist: true);
        PharmacySetup completed = await CreateFulfillmentAsync(
            FulfillmentStatus.Dispensed,
            assignedToCurrentPharmacist: true);
        PharmacySetup assignedElsewhere = await CreateFulfillmentAsync(
            FulfillmentStatus.InReview,
            assignedToCurrentPharmacist: false);

        using HttpRequestMessage request = CreatePharmacistRequest(
            HttpMethod.Get,
            "/api/v1/fulfillments?pageSize=50");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        PharmacyWorkQueuePageResponse queue = await response.Content
            .ReadFromJsonAsync<PharmacyWorkQueuePageResponse>()
            ?? throw new InvalidOperationException("The pharmacy queue response was missing.");

        Assert.Contains(queue.Items, item => item.FulfillmentId == pending.FulfillmentId);
        Assert.Contains(queue.Items, item => item.FulfillmentId == inReview.FulfillmentId);
        Assert.Contains(queue.Items, item => item.FulfillmentId == ready.FulfillmentId);
        Assert.DoesNotContain(queue.Items, item => item.FulfillmentId == completed.FulfillmentId);
        Assert.DoesNotContain(
            queue.Items,
            item => item.FulfillmentId == assignedElsewhere.FulfillmentId);

        int readyPosition = Array.FindIndex(
            queue.Items.ToArray(),
            item => item.FulfillmentId == ready.FulfillmentId);
        int inReviewPosition = Array.FindIndex(
            queue.Items.ToArray(),
            item => item.FulfillmentId == inReview.FulfillmentId);
        int pendingPosition = Array.FindIndex(
            queue.Items.ToArray(),
            item => item.FulfillmentId == pending.FulfillmentId);
        Assert.True(readyPosition < inReviewPosition);
        Assert.True(inReviewPosition < pendingPosition);

        Assert.DoesNotContain("instructions", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allergy", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth-test|", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplicitHistoryFilterReturnsOnlyCurrentPharmacistHistory()
    {
        PharmacySetup ownDispensed = await CreateFulfillmentAsync(
            FulfillmentStatus.Dispensed,
            assignedToCurrentPharmacist: true);
        PharmacySetup otherDispensed = await CreateFulfillmentAsync(
            FulfillmentStatus.Dispensed,
            assignedToCurrentPharmacist: false);

        using HttpRequestMessage request = CreatePharmacistRequest(
            HttpMethod.Get,
            "/api/v1/fulfillments?status=Dispensed&pageSize=50");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PharmacyWorkQueuePageResponse queue = await response.Content
            .ReadFromJsonAsync<PharmacyWorkQueuePageResponse>()
            ?? throw new InvalidOperationException("The pharmacy history response was missing.");
        Assert.Contains(queue.Items, item => item.FulfillmentId == ownDispensed.FulfillmentId);
        Assert.DoesNotContain(
            queue.Items,
            item => item.FulfillmentId == otherDispensed.FulfillmentId);
    }

    [Fact]
    public async Task DetailReturnsMinimumPharmacyContextAndConcealsOtherAssignments()
    {
        PharmacySetup own = await CreateFulfillmentAsync(
            FulfillmentStatus.InReview,
            assignedToCurrentPharmacist: true);
        PharmacySetup assignedElsewhere = await CreateFulfillmentAsync(
            FulfillmentStatus.Ready,
            assignedToCurrentPharmacist: false);

        using HttpRequestMessage ownRequest = CreatePharmacistRequest(
            HttpMethod.Get,
            $"/api/v1/fulfillments/{own.FulfillmentId}");
        HttpResponseMessage ownResponse = await client.SendAsync(ownRequest);
        using HttpRequestMessage otherRequest = CreatePharmacistRequest(
            HttpMethod.Get,
            $"/api/v1/fulfillments/{assignedElsewhere.FulfillmentId}");
        HttpResponseMessage otherResponse = await client.SendAsync(otherRequest);

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        PharmacyFulfillmentResponse detail = await ownResponse.Content
            .ReadFromJsonAsync<PharmacyFulfillmentResponse>()
            ?? throw new InvalidOperationException("The fulfillment response was missing.");
        Assert.Equal(own.FulfillmentId, detail.Id);
        Assert.Equal("InReview", detail.Status);
        Assert.True(detail.AssignedToCurrentPharmacist);
        Assert.Equal(AuthTestIdentities.PatientDisplayName, detail.Patient.DisplayName);
        Assert.Equal(own.MedicationDisplayName, detail.Prescription.MedicationDisplayName);
        Assert.Equal("Take once daily.", detail.Prescription.Instructions);
        Assert.Equal(AuthTestIdentities.DoctorDisplayName, detail.Prescription.PrescriberDisplayName);
        Assert.True(detail.Version > 0);

        string detailJson = await ownResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("AUTH-MRN-001", detailJson, StringComparison.Ordinal);
        Assert.DoesNotContain("clinicalNotes", detailJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth-test|", detailJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    [Fact]
    public async Task InvalidStatusAndPaginationReturnStableProblemCodes()
    {
        using HttpRequestMessage statusRequest = CreatePharmacistRequest(
            HttpMethod.Get,
            "/api/v1/fulfillments?status=Unknown");
        HttpResponseMessage statusResponse = await client.SendAsync(statusRequest);
        using HttpRequestMessage pageRequest = CreatePharmacistRequest(
            HttpMethod.Get,
            $"/api/v1/fulfillments?page={int.MaxValue}&pageSize=50");
        HttpResponseMessage pageResponse = await client.SendAsync(pageRequest);

        Assert.Equal(HttpStatusCode.BadRequest, statusResponse.StatusCode);
        Assert.Equal(
            "invalid_fulfillment_status",
            await ReadErrorCodeAsync(statusResponse));
        Assert.Equal(HttpStatusCode.BadRequest, pageResponse.StatusCode);
        Assert.Equal("invalid_pagination", await ReadErrorCodeAsync(pageResponse));
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private async Task<PharmacySetup> CreateFulfillmentAsync(
        FulfillmentStatus status,
        bool? assignedToCurrentPharmacist = null)
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
        long currentPharmacistId = await context.PharmacistProfiles
            .Where(profile =>
                profile.UserProfile.Auth0Subject == AuthTestIdentities.PharmacistSubject)
            .Select(profile => profile.Id)
            .SingleAsync();
        long otherPharmacistId = await context.PharmacistProfiles
            .Where(profile => profile.Id != currentPharmacistId)
            .Select(profile => profile.Id)
            .FirstAsync();

        string unique = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = AuthTestClock.UtcNow
            .AddDays(-3)
            .AddSeconds(Random.Shared.Next(1, 80_000));
        Medication medication = new()
        {
            RxCui = unique[..18],
            DisplayName = $"Pharmacy medicine {unique[..8]}",
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
            Reason = "Synthetic pharmacy review",
            Status = AppointmentStatus.Completed,
            CreatedAtUtc = startedAt.AddDays(-1),
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        Consultation consultation = new()
        {
            AppointmentId = appointment.Id,
            Outcome = "Stable",
            ClinicalNotes = "Concealed synthetic clinical note",
            PatientSummary = "Synthetic patient summary",
            CareInstructions = "Follow the synthetic care plan",
            Status = ConsultationStatus.Completed,
            StartedAtUtc = startedAt,
            CompletedAtUtc = startedAt.AddMinutes(30),
            CreatedAtUtc = startedAt,
        };
        context.Consultations.Add(consultation);
        await context.SaveChangesAsync();

        DateTimeOffset issuedAt = consultation.CompletedAtUtc!.Value.AddMinutes(1);
        DateTimeOffset fulfillmentCreatedAt = issuedAt.AddMinutes(1);
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
            CancelledAtUtc = isCancelled ? fulfillmentCreatedAt.AddMinutes(4) : null,
        };
        context.Prescriptions.Add(prescription);
        await context.SaveChangesAsync();

        bool isAssigned = status != FulfillmentStatus.Pending;
        long? assignedPharmacistId = !isAssigned
            ? null
            : assignedToCurrentPharmacist == true
                ? currentPharmacistId
                : otherPharmacistId;
        DateTimeOffset? reviewedAt = isAssigned
            ? fulfillmentCreatedAt.AddMinutes(1)
            : null;
        DateTimeOffset? readyAt = status is FulfillmentStatus.Ready or
            FulfillmentStatus.Dispensed
            ? fulfillmentCreatedAt.AddMinutes(2)
            : null;
        Fulfillment fulfillment = new()
        {
            PrescriptionId = prescription.Id,
            AssignedPharmacistProfileId = assignedPharmacistId,
            Status = status,
            CreatedAtUtc = fulfillmentCreatedAt,
            ReviewStartedAtUtc = reviewedAt,
            ReadyAtUtc = readyAt,
            DispensedAtUtc = status == FulfillmentStatus.Dispensed
                ? fulfillmentCreatedAt.AddMinutes(3)
                : null,
            CancelledAtUtc = isCancelled
                ? fulfillmentCreatedAt.AddMinutes(4)
                : null,
        };
        context.Fulfillments.Add(fulfillment);
        await context.SaveChangesAsync();

        return new PharmacySetup(fulfillment.Id, medication.DisplayName);
    }

    private HttpRequestMessage CreatePharmacistRequest(
        HttpMethod method,
        string path) =>
        CreateAuthenticatedRequest(
            method,
            path,
            AuthTestIdentities.PharmacistSubject,
            ApplicationRoles.Pharmacist);

    private HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string path,
        string subject,
        string role)
    {
        string token = factory.CreateToken(
        [
            new Claim("sub", subject),
            new Claim(AuthenticationApiFactory.RoleClaim, role),
        ]);
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errorCode").GetString();
    }

    private sealed record PharmacySetup(
        long FulfillmentId,
        string MedicationDisplayName);
}
