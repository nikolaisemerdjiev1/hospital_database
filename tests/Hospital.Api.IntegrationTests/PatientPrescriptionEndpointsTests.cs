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
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class PatientPrescriptionEndpointsTests(AuthenticationDatabaseFixture database)
{
    [Fact]
    public async Task PrescriptionCollectionRequiresAnAuthorizedPatient()
    {
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);

        HttpResponseMessage missingToken = await client.GetAsync("/api/v1/prescriptions");
        using HttpRequestMessage doctorRequest = CreateRequest(
            factory,
            "/api/v1/prescriptions",
            AuthTestIdentities.DoctorSubject,
            ApplicationRoles.Doctor);
        HttpResponseMessage doctorResponse = await client.SendAsync(doctorRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, doctorResponse.StatusCode);
    }

    [Fact]
    public async Task CollectionReturnsOnlyOwnedPatientSafeStatusLabels()
    {
        TestPatient owner = await CreatePatientAsync();
        TestPatient otherPatient = await CreatePatientAsync();
        DateTimeOffset issuedAt = AuthTestClock.UtcNow.AddDays(-1);
        PatientPrescriptionSetup pending = await CreatePrescriptionAsync(
            owner.ProfileId,
            FulfillmentStatus.Pending,
            issuedAt.AddMinutes(1));
        PatientPrescriptionSetup inReview = await CreatePrescriptionAsync(
            owner.ProfileId,
            FulfillmentStatus.InReview,
            issuedAt.AddMinutes(2));
        PatientPrescriptionSetup ready = await CreatePrescriptionAsync(
            owner.ProfileId,
            FulfillmentStatus.Ready,
            issuedAt.AddMinutes(3));
        PatientPrescriptionSetup dispensed = await CreatePrescriptionAsync(
            owner.ProfileId,
            FulfillmentStatus.Dispensed,
            issuedAt.AddMinutes(4));
        PatientPrescriptionSetup cancelled = await CreatePrescriptionAsync(
            owner.ProfileId,
            FulfillmentStatus.Cancelled,
            issuedAt.AddMinutes(5));
        PatientPrescriptionSetup other = await CreatePrescriptionAsync(
            otherPatient.ProfileId,
            FulfillmentStatus.Ready,
            issuedAt.AddMinutes(6));
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);
        using HttpRequestMessage request = CreatePatientRequest(
            factory,
            "/api/v1/prescriptions?pageSize=50",
            owner.Subject);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        PatientPrescriptionPageResponse page = await response.Content
            .ReadFromJsonAsync<PatientPrescriptionPageResponse>()
            ?? throw new InvalidOperationException("The patient prescription page was missing.");
        Assert.Equal(5, page.TotalItems);
        Assert.Equal(5, page.Items.Count);
        Assert.DoesNotContain(page.Items, item => item.Id == other.PrescriptionId);

        Dictionary<long, PatientPrescriptionResponse> byId = page.Items.ToDictionary(item => item.Id);
        Assert.Equal(
            PatientPharmacyStatusLabels.ReceivedByPharmacy,
            byId[pending.PrescriptionId].PharmacyStatus);
        Assert.Equal(
            PatientPharmacyStatusLabels.UnderPharmacistReview,
            byId[inReview.PrescriptionId].PharmacyStatus);
        Assert.Equal(
            PatientPharmacyStatusLabels.ReadyForPickup,
            byId[ready.PrescriptionId].PharmacyStatus);
        Assert.Equal(
            PatientPharmacyStatusLabels.Dispensed,
            byId[dispensed.PrescriptionId].PharmacyStatus);
        Assert.Equal(
            PatientPharmacyStatusLabels.Cancelled,
            byId[cancelled.PrescriptionId].PharmacyStatus);
        Assert.Equal(
            ready.MedicationDisplayName,
            byId[ready.PrescriptionId].MedicationDisplayName);
        Assert.DoesNotContain(other.MedicationDisplayName, json, StringComparison.Ordinal);

        string[] concealedProperties =
        [
            "assignedPharmacist",
            "fulfillmentId",
            "version",
            "reviewStartedAtUtc",
            "readyAtUtc",
            "dispensedAtUtc",
            "rxCui",
            "consultationId",
            "patientProfileId",
            "clinicalNotes",
            "medicalRecordNumber",
            "auth-test|",
            "AUTH-PHR-001",
            AuthTestIdentities.PharmacistDisplayName,
        ];
        Assert.All(concealedProperties, concealed =>
            Assert.DoesNotContain(concealed, json, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("\"pharmacyStatus\":\"Pending\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"pharmacyStatus\":\"InReview\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"pharmacyStatus\":\"Ready\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectionIsNewestFirstBoundedAndReturnsStablePaginationErrors()
    {
        TestPatient patient = await CreatePatientAsync();
        DateTimeOffset baseline = AuthTestClock.UtcNow.AddDays(-2);
        PatientPrescriptionSetup oldest = await CreatePrescriptionAsync(
            patient.ProfileId,
            FulfillmentStatus.Pending,
            baseline.AddHours(1));
        PatientPrescriptionSetup middle = await CreatePrescriptionAsync(
            patient.ProfileId,
            FulfillmentStatus.InReview,
            baseline.AddHours(2));
        PatientPrescriptionSetup newest = await CreatePrescriptionAsync(
            patient.ProfileId,
            FulfillmentStatus.Ready,
            baseline.AddHours(3));
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using HttpClient client = CreateClient(factory);

        using HttpRequestMessage firstRequest = CreatePatientRequest(
            factory,
            "/api/v1/prescriptions?page=1&pageSize=2",
            patient.Subject);
        HttpResponseMessage firstResponse = await client.SendAsync(firstRequest);
        using HttpRequestMessage secondRequest = CreatePatientRequest(
            factory,
            "/api/v1/prescriptions?page=2&pageSize=2",
            patient.Subject);
        HttpResponseMessage secondResponse = await client.SendAsync(secondRequest);
        using HttpRequestMessage invalidRequest = CreatePatientRequest(
            factory,
            $"/api/v1/prescriptions?page={int.MaxValue}&pageSize=50",
            patient.Subject);
        HttpResponseMessage invalidResponse = await client.SendAsync(invalidRequest);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        PatientPrescriptionPageResponse firstPage = await firstResponse.Content
            .ReadFromJsonAsync<PatientPrescriptionPageResponse>()
            ?? throw new InvalidOperationException("The first prescription page was missing.");
        Assert.Equal(3, firstPage.TotalItems);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(
            new[] { newest.PrescriptionId, middle.PrescriptionId },
            firstPage.Items.Select(item => item.Id));

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        PatientPrescriptionPageResponse secondPage = await secondResponse.Content
            .ReadFromJsonAsync<PatientPrescriptionPageResponse>()
            ?? throw new InvalidOperationException("The second prescription page was missing.");
        PatientPrescriptionResponse remaining = Assert.Single(secondPage.Items);
        Assert.Equal(oldest.PrescriptionId, remaining.Id);

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("invalid_pagination", await ReadErrorCodeAsync(invalidResponse));
    }

    private async Task<TestPatient> CreatePatientAsync()
    {
        string unique = Guid.NewGuid().ToString("N");
        string subject = $"auth-test|patient-{unique}";
        await using ApplicationDbContext context = database.CreateContext();
        UserProfile userProfile = new()
        {
            Auth0Subject = subject,
            DisplayName = $"Patient {unique[..8]}",
            ProfileType = ProfileType.Patient,
            Status = AccountStatus.Active,
            CreatedAtUtc = AuthTestClock.UtcNow,
        };
        context.UserProfiles.Add(userProfile);
        await context.SaveChangesAsync();

        PatientProfile profile = new()
        {
            UserProfileId = userProfile.Id,
            MedicalRecordNumber = $"MRN-{unique[..16]}",
            DateOfBirth = new DateOnly(1990, 1, 1),
            CreatedAtUtc = AuthTestClock.UtcNow,
        };
        context.PatientProfiles.Add(profile);
        await context.SaveChangesAsync();
        return new TestPatient(subject, profile.Id);
    }

    private async Task<PatientPrescriptionSetup> CreatePrescriptionAsync(
        long patientProfileId,
        FulfillmentStatus fulfillmentStatus,
        DateTimeOffset issuedAtUtc)
    {
        await using ApplicationDbContext context = database.CreateContext();
        long clinicianProfileId = await context.ClinicianProfiles
            .Where(profile =>
                profile.UserProfile.Auth0Subject == AuthTestIdentities.DoctorSubject)
            .Select(profile => profile.Id)
            .SingleAsync();
        long pharmacistProfileId = await context.PharmacistProfiles
            .Where(profile =>
                profile.UserProfile.Auth0Subject == AuthTestIdentities.PharmacistSubject)
            .Select(profile => profile.Id)
            .SingleAsync();
        string unique = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAtUtc = issuedAtUtc
            .AddHours(-1)
            .AddSeconds(Random.Shared.Next(1, 50));
        Medication medication = new()
        {
            RxCui = unique[..18],
            DisplayName = $"Patient medicine {unique[..8]}",
            Classification = "SCD",
            Strength = "10 mg",
            DoseForm = "Oral tablet",
            Source = MedicationSource.RxNorm,
            LastVerifiedAtUtc = issuedAtUtc,
            CreatedAtUtc = issuedAtUtc,
        };
        context.Medications.Add(medication);

        AvailabilitySlot slot = new()
        {
            ClinicianProfileId = clinicianProfileId,
            StartsAtUtc = startedAtUtc,
            EndsAtUtc = startedAtUtc.AddMinutes(45),
            CreatedAtUtc = startedAtUtc.AddDays(-1),
        };
        context.AvailabilitySlots.Add(slot);
        await context.SaveChangesAsync();

        Appointment appointment = new()
        {
            PatientProfileId = patientProfileId,
            AvailabilitySlotId = slot.Id,
            Reason = "Synthetic patient medication status",
            Status = AppointmentStatus.Completed,
            CreatedAtUtc = startedAtUtc.AddDays(-1),
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        Consultation consultation = new()
        {
            AppointmentId = appointment.Id,
            Outcome = "Stable",
            ClinicalNotes = "Concealed patient prescription note",
            PatientSummary = "Synthetic patient summary",
            CareInstructions = "Follow the synthetic plan",
            Status = ConsultationStatus.Completed,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = issuedAtUtc.AddMinutes(-1),
            CreatedAtUtc = startedAtUtc,
        };
        context.Consultations.Add(consultation);
        await context.SaveChangesAsync();

        bool isCancelled = fulfillmentStatus == FulfillmentStatus.Cancelled;
        Prescription prescription = new()
        {
            ConsultationId = consultation.Id,
            MedicationId = medication.Id,
            PrescriberClinicianProfileId = clinicianProfileId,
            PatientProfileId = patientProfileId,
            RxCuiSnapshot = medication.RxCui,
            MedicationDisplayNameSnapshot = medication.DisplayName,
            Dose = "10 mg",
            Instructions = "Take once daily.",
            Quantity = 30,
            Status = isCancelled ? PrescriptionStatus.Cancelled : PrescriptionStatus.Issued,
            IssuedAtUtc = issuedAtUtc,
            CancelledAtUtc = isCancelled ? issuedAtUtc.AddMinutes(4) : null,
        };
        context.Prescriptions.Add(prescription);
        await context.SaveChangesAsync();

        bool hasReview = fulfillmentStatus != FulfillmentStatus.Pending;
        Fulfillment fulfillment = new()
        {
            PrescriptionId = prescription.Id,
            AssignedPharmacistProfileId = hasReview ? pharmacistProfileId : null,
            Status = fulfillmentStatus,
            CreatedAtUtc = issuedAtUtc,
            ReviewStartedAtUtc = hasReview ? issuedAtUtc.AddMinutes(1) : null,
            ReadyAtUtc = fulfillmentStatus is FulfillmentStatus.Ready or
                FulfillmentStatus.Dispensed
                ? issuedAtUtc.AddMinutes(2)
                : null,
            DispensedAtUtc = fulfillmentStatus == FulfillmentStatus.Dispensed
                ? issuedAtUtc.AddMinutes(3)
                : null,
            CancelledAtUtc = isCancelled ? issuedAtUtc.AddMinutes(4) : null,
        };
        context.Fulfillments.Add(fulfillment);
        await context.SaveChangesAsync();

        return new PatientPrescriptionSetup(prescription.Id, medication.DisplayName);
    }

    private static HttpClient CreateClient(AuthenticationApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static HttpRequestMessage CreatePatientRequest(
        AuthenticationApiFactory factory,
        string path,
        string subject) =>
        CreateRequest(factory, path, subject, ApplicationRoles.Patient);

    private static HttpRequestMessage CreateRequest(
        AuthenticationApiFactory factory,
        string path,
        string subject,
        string role)
    {
        string token = factory.CreateToken(
        [
            new Claim("sub", subject),
            new Claim(AuthenticationApiFactory.RoleClaim, role),
        ]);
        HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errorCode").GetString();
    }

    private sealed record TestPatient(string Subject, long ProfileId);

    private sealed record PatientPrescriptionSetup(
        long PrescriptionId,
        string MedicationDisplayName);
}
