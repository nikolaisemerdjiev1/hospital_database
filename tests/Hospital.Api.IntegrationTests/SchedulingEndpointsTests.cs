using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class SchedulingEndpointsTests : IDisposable
{
    private readonly AuthenticationDatabaseFixture database;
    private readonly AuthenticationApiFactory factory;
    private readonly HttpClient client;

    public SchedulingEndpointsTests(AuthenticationDatabaseFixture database)
    {
        this.database = database;
        factory = new AuthenticationApiFactory(database.ConnectionString);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task SchedulingEndpointsRequireAnAuthorizedPatient()
    {
        HttpResponseMessage missingToken = await client.GetAsync("/api/v1/clinicians");
        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);

        using HttpRequestMessage doctorRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/v1/clinicians",
            AuthTestIdentities.DoctorSubject,
            ApplicationRoles.Doctor);
        HttpResponseMessage doctorResponse = await client.SendAsync(doctorRequest);
        Assert.Equal(HttpStatusCode.Forbidden, doctorResponse.StatusCode);
    }

    [Fact]
    public async Task PatientCanListActiveCliniciansWithoutIdentityOrClinicalLeaks()
    {
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Get,
            "/api/v1/clinicians");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        ClinicianResponse[] clinicians = JsonSerializer.Deserialize<ClinicianResponse[]>(
            json,
            JsonOptions) ?? [];
        ClinicianResponse clinician = Assert.Single(clinicians);
        Assert.Equal(AuthTestIdentities.DoctorDisplayName, clinician.DisplayName);
        Assert.Equal("Test Medicine", clinician.Specialty);
        Assert.DoesNotContain(AuthTestIdentities.DoctorSubject, json, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTH-DOC", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AvailabilityReturnsOnlyFutureUnbookedSlots()
    {
        DateTimeOffset start = new(2031, 2, 4, 16, 0, 0, TimeSpan.Zero);
        (AvailabilitySlot available, AvailabilitySlot booked) = await CreateAvailabilityPairAsync(
            start);
        await CreateAppointmentAsync(booked, AuthTestIdentities.PatientSubject);

        string path = $"/api/v1/clinicians/{available.ClinicianProfileId}/availability" +
            $"?from={Uri.EscapeDataString(start.AddDays(-1).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(start.AddDays(2).ToString("O"))}";
        using HttpRequestMessage request = CreatePatientRequest(HttpMethod.Get, path);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AvailabilityResponse[] slots = await response.Content
            .ReadFromJsonAsync<AvailabilityResponse[]>() ?? [];
        AvailabilityResponse returned = Assert.Single(slots);
        Assert.Equal(available.Id, returned.Id);
        Assert.Equal(available.Version, returned.Version);
    }

    [Fact]
    public async Task PatientCanBookAndThenListTheirAppointment()
    {
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            new DateTimeOffset(2031, 3, 5, 17, 0, 0, TimeSpan.Zero));
        BookAppointmentRequest body = new(slot.Id, slot.Version, "  Annual wellness visit  ");
        using HttpRequestMessage bookingRequest = CreatePatientRequest(
            HttpMethod.Post,
            "/api/v1/appointments",
            body);

        HttpResponseMessage bookingResponse = await client.SendAsync(bookingRequest);

        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        AppointmentResponse booked = await bookingResponse.Content
            .ReadFromJsonAsync<AppointmentResponse>()
            ?? throw new InvalidOperationException("The booking response was missing.");
        Assert.Equal("Annual wellness visit", booked.Reason);
        Assert.Equal(nameof(AppointmentStatus.Scheduled), booked.Status);
        Assert.True(booked.Version > 0);

        using HttpRequestMessage listRequest = CreatePatientRequest(
            HttpMethod.Get,
            "/api/v1/appointments");
        HttpResponseMessage listResponse = await client.SendAsync(listRequest);
        string json = await listResponse.Content.ReadAsStringAsync();
        AppointmentPageResponse page = JsonSerializer.Deserialize<AppointmentPageResponse>(
            json,
            JsonOptions)
            ?? throw new InvalidOperationException("The appointment page was missing.");

        Assert.Contains(page.Items, appointment => appointment.Id == booked.Id);
        Assert.DoesNotContain(AuthTestIdentities.PatientSubject, json, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTH-MRN", json, StringComparison.Ordinal);
        Assert.DoesNotContain("dateOfBirth", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allerg", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RebookingAnActiveSlotReturnsConflictProblemDetails()
    {
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            new DateTimeOffset(2031, 4, 6, 18, 0, 0, TimeSpan.Zero));
        await CreateAppointmentAsync(slot, AuthTestIdentities.PatientSubject);
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Post,
            "/api/v1/appointments",
            new BookAppointmentRequest(slot.Id, slot.Version, "Follow-up"));

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "availability_taken");
    }

    [Fact]
    public async Task PatientCanCancelOwnedFutureAppointmentAndStaleRetryConflicts()
    {
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            new DateTimeOffset(2031, 5, 7, 19, 0, 0, TimeSpan.Zero));
        Appointment appointment = await CreateAppointmentAsync(
            slot,
            AuthTestIdentities.PatientSubject);
        TransitionAppointmentRequest body = new("Cancelled", appointment.Version, "Schedule conflict");

        using HttpRequestMessage firstRequest = CreatePatientRequest(
            HttpMethod.Post,
            $"/api/v1/appointments/{appointment.Id}/transitions",
            body);
        HttpResponseMessage firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        AppointmentResponse cancelled = await firstResponse.Content
            .ReadFromJsonAsync<AppointmentResponse>()
            ?? throw new InvalidOperationException("The cancellation response was missing.");
        Assert.Equal(nameof(AppointmentStatus.Cancelled), cancelled.Status);
        Assert.NotNull(cancelled.CancelledAtUtc);

        using HttpRequestMessage retryRequest = CreatePatientRequest(
            HttpMethod.Post,
            $"/api/v1/appointments/{appointment.Id}/transitions",
            body);
        HttpResponseMessage retryResponse = await client.SendAsync(retryRequest);
        await AssertProblemAsync(retryResponse, HttpStatusCode.Conflict, "appointment_changed");
    }

    [Fact]
    public async Task AnotherPatientsAppointmentLooksMissing()
    {
        const string otherSubject = "auth-test|scheduling-other-patient";
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            new DateTimeOffset(2031, 6, 8, 20, 0, 0, TimeSpan.Zero));
        await EnsurePatientAsync(otherSubject, "Scheduling Other Patient", "AUTH-MRN-100");
        Appointment appointment = await CreateAppointmentAsync(slot, otherSubject);
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Post,
            $"/api/v1/appointments/{appointment.Id}/transitions",
            new TransitionAppointmentRequest("Cancelled", appointment.Version, null));

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "appointment_not_found");
    }

    [Fact]
    public async Task InvalidBookingReasonReturnsBadRequestProblemDetails()
    {
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            new DateTimeOffset(2031, 7, 9, 21, 0, 0, TimeSpan.Zero));
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Post,
            "/api/v1/appointments",
            new BookAppointmentRequest(slot.Id, slot.Version, "   "));

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_appointment_reason");
    }

    [Fact]
    public async Task OverlappingPatientAppointmentReturnsConflictProblemDetails()
    {
        DateTimeOffset existingStart = new(2031, 8, 10, 16, 0, 0, TimeSpan.Zero);
        AvailabilitySlot existingSlot = await CreateAvailabilityAsync(existingStart);
        await CreateAppointmentAsync(existingSlot, AuthTestIdentities.PatientSubject);
        AvailabilitySlot overlappingSlot = await CreateAvailabilityAsync(
            existingStart.AddMinutes(30));
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Post,
            "/api/v1/appointments",
            new BookAppointmentRequest(
                overlappingSlot.Id,
                overlappingSlot.Version,
                "Overlapping visit"));

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "appointment_overlap");
    }

    [Fact]
    public async Task StartedAvailabilityCannotBeBooked()
    {
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            AuthTestClock.UtcNow.AddMinutes(-30));
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Post,
            "/api/v1/appointments",
            new BookAppointmentRequest(slot.Id, slot.Version, "Late booking"));

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "availability_started");
    }

    [Fact]
    public async Task StaleAvailabilityVersionReturnsConflictProblemDetails()
    {
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            new DateTimeOffset(2031, 9, 11, 17, 0, 0, TimeSpan.Zero));
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Post,
            "/api/v1/appointments",
            new BookAppointmentRequest(slot.Id, slot.Version + 1, "Stale booking"));

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "availability_changed");
    }

    [Fact]
    public async Task PatientCannotUseAnUnsupportedAppointmentTransition()
    {
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            new DateTimeOffset(2031, 10, 12, 18, 0, 0, TimeSpan.Zero));
        Appointment appointment = await CreateAppointmentAsync(
            slot,
            AuthTestIdentities.PatientSubject);
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Post,
            $"/api/v1/appointments/{appointment.Id}/transitions",
            new TransitionAppointmentRequest(
                nameof(AppointmentStatus.Completed),
                appointment.Version,
                null));

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "unsupported_transition");
    }

    [Fact]
    public async Task StartedAppointmentCannotBeCancelled()
    {
        AvailabilitySlot slot = await CreateAvailabilityAsync(
            AuthTestClock.UtcNow.AddMinutes(-15));
        Appointment appointment = await CreateAppointmentAsync(
            slot,
            AuthTestIdentities.PatientSubject);
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Post,
            $"/api/v1/appointments/{appointment.Id}/transitions",
            new TransitionAppointmentRequest(
                nameof(AppointmentStatus.Cancelled),
                appointment.Version,
                null));

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "appointment_started");
    }

    [Fact]
    public async Task PaginationOutsideTheSupportedRangeReturnsBadRequest()
    {
        using HttpRequestMessage request = CreatePatientRequest(
            HttpMethod.Get,
            "/api/v1/appointments?page=2147483647&pageSize=50");

        HttpResponseMessage response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_pagination");
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpRequestMessage CreatePatientRequest(
        HttpMethod method,
        string path,
        object? body = null) =>
        CreateAuthenticatedRequest(
            method,
            path,
            AuthTestIdentities.PatientSubject,
            ApplicationRoles.Patient,
            body);

    private HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string path,
        string subject,
        string role,
        object? body = null)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateToken(
            [
                new Claim("sub", subject),
                new Claim(AuthenticationApiFactory.RoleClaim, role),
            ]));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<AvailabilitySlot> CreateAvailabilityAsync(DateTimeOffset startsAtUtc)
    {
        await using ApplicationDbContext context = database.CreateContext();
        long clinicianId = await context.ClinicianProfiles
            .Where(profile => profile.UserProfile.Auth0Subject == AuthTestIdentities.DoctorSubject)
            .Select(profile => profile.Id)
            .SingleAsync();
        AvailabilitySlot slot = new()
        {
            ClinicianProfileId = clinicianId,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = startsAtUtc.AddMinutes(45),
            CreatedAtUtc = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        context.AvailabilitySlots.Add(slot);
        await context.SaveChangesAsync();
        return slot;
    }

    private async Task<(AvailabilitySlot Available, AvailabilitySlot Booked)>
        CreateAvailabilityPairAsync(DateTimeOffset startsAtUtc)
    {
        AvailabilitySlot available = await CreateAvailabilityAsync(startsAtUtc);
        AvailabilitySlot booked = await CreateAvailabilityAsync(startsAtUtc.AddHours(2));
        return (available, booked);
    }

    private async Task<Appointment> CreateAppointmentAsync(
        AvailabilitySlot slot,
        string patientSubject)
    {
        await using ApplicationDbContext context = database.CreateContext();
        long patientId = await context.PatientProfiles
            .Where(profile => profile.UserProfile.Auth0Subject == patientSubject)
            .Select(profile => profile.Id)
            .SingleAsync();
        Appointment appointment = new()
        {
            PatientProfileId = patientId,
            AvailabilitySlotId = slot.Id,
            Reason = "Existing synthetic appointment",
            Status = AppointmentStatus.Scheduled,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return appointment;
    }

    private async Task EnsurePatientAsync(
        string subject,
        string displayName,
        string medicalRecordNumber)
    {
        await using ApplicationDbContext context = database.CreateContext();
        if (await context.UserProfiles.AnyAsync(profile => profile.Auth0Subject == subject))
        {
            return;
        }

        DateTimeOffset createdAtUtc = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        UserProfile user = new()
        {
            Auth0Subject = subject,
            DisplayName = displayName,
            ProfileType = ProfileType.Patient,
            Status = AccountStatus.Active,
            CreatedAtUtc = createdAtUtc,
        };
        context.UserProfiles.Add(user);
        await context.SaveChangesAsync();
        context.PatientProfiles.Add(new PatientProfile
        {
            UserProfileId = user.Id,
            MedicalRecordNumber = medicalRecordNumber,
            DateOfBirth = new DateOnly(1991, 1, 1),
            CreatedAtUtc = createdAtUtc,
        });
        await context.SaveChangesAsync();
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(expectedErrorCode, problem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
