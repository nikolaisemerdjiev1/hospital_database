using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Audit;
using Hospital.Core.Consultations;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;
using Hospital.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class ClinicalEndpointsTests : IDisposable
{
    private readonly AuthenticationDatabaseFixture database;
    private readonly AuthenticationApiFactory factory;
    private readonly HttpClient client;

    public ClinicalEndpointsTests(AuthenticationDatabaseFixture database)
    {
        this.database = database;
        factory = new AuthenticationApiFactory(database.ConnectionString);
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task ClinicalEndpointsRequireAnAuthorizedDoctor()
    {
        const string path = "/api/v1/clinical-worklist" +
            "?from=2030-01-15T00:00:00Z&to=2030-02-15T00:00:00Z";

        HttpResponseMessage missingToken = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);

        using HttpRequestMessage patientRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            path,
            AuthTestIdentities.PatientSubject,
            ApplicationRoles.Patient);
        HttpResponseMessage patientResponse = await client.SendAsync(patientRequest);
        Assert.Equal(HttpStatusCode.Forbidden, patientResponse.StatusCode);
    }

    [Fact]
    public async Task DoctorWorklistContainsOnlyAssignedAppointmentsAndMinimumPatientContext()
    {
        Appointment assigned = await CreateAppointmentAsync(
            AuthTestIdentities.DoctorSubject,
            new DateTimeOffset(2030, 2, 2, 16, 0, 0, TimeSpan.Zero),
            "Assigned clinical review");
        Appointment otherDoctor = await CreateAppointmentAsync(
            AuthTestIdentities.OtherDoctorSubject,
            new DateTimeOffset(2030, 2, 2, 17, 0, 0, TimeSpan.Zero),
            "Concealed clinical review");

        const string path = "/api/v1/clinical-worklist" +
            "?from=2030-02-01T00:00:00Z&to=2030-03-01T00:00:00Z&pageSize=50";
        using HttpRequestMessage request = CreateDoctorRequest(HttpMethod.Get, path);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        DoctorWorklistPageResponse worklist = await response.Content
            .ReadFromJsonAsync<DoctorWorklistPageResponse>()
            ?? throw new InvalidOperationException("The worklist response was missing.");
        DoctorWorklistItemResponse item = Assert.Single(
            worklist.Items,
            candidate => candidate.AppointmentId == assigned.Id);
        Assert.Equal("Assigned clinical review", item.Reason);
        Assert.DoesNotContain(
            worklist.Items,
            candidate => candidate.AppointmentId == otherDoctor.Id);
        Assert.DoesNotContain(AuthTestIdentities.DoctorSubject, json, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTH-MRN-001", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorCanStartSaveAndCompleteAssignedConsultation()
    {
        Appointment appointment = await CreateAppointmentAsync(
            AuthTestIdentities.DoctorSubject,
            new DateTimeOffset(2030, 3, 4, 16, 0, 0, TimeSpan.Zero),
            "Consultation lifecycle");

        using HttpRequestMessage startRequest = CreateDoctorRequest(
            HttpMethod.Post,
            $"/api/v1/appointments/{appointment.Id}/consultations",
            new StartConsultationRequest(appointment.Version));
        HttpResponseMessage startResponse = await client.SendAsync(startRequest);

        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        Assert.NotNull(startResponse.Headers.Location);
        DoctorConsultationResponse started = await ReadConsultationAsync(startResponse);
        Assert.Equal(nameof(ConsultationStatus.Draft), started.Status);
        Assert.Equal(nameof(AppointmentStatus.InProgress), started.Appointment.Status);
        Assert.Equal(AuthTestIdentities.PatientDisplayName, started.Patient.DisplayName);
        Assert.Equal("AUTH-MRN-001", started.Patient.MedicalRecordNumber);
        Assert.True(started.Version > 0);

        SaveConsultationDraftRequest draft = new(
            "  Routine follow-up  ",
            "  Private synthetic notes  ",
            "  Your visit was completed.  ",
            "  Continue the fictional care plan.  ",
            started.Version);
        using HttpRequestMessage saveRequest = CreateDoctorRequest(
            HttpMethod.Put,
            $"/api/v1/consultations/{started.Id}",
            draft);
        HttpResponseMessage saveResponse = await client.SendAsync(saveRequest);

        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
        DoctorConsultationResponse saved = await ReadConsultationAsync(saveResponse);
        Assert.Equal("Routine follow-up", saved.Outcome);
        Assert.Equal("Private synthetic notes", saved.ClinicalNotes);
        Assert.True(saved.Version > started.Version);

        CompleteConsultationRequest completion = new(
            saved.Outcome,
            saved.ClinicalNotes,
            saved.PatientSummary,
            saved.CareInstructions,
            saved.Version);
        using HttpRequestMessage completeRequest = CreateDoctorRequest(
            HttpMethod.Post,
            $"/api/v1/consultations/{saved.Id}/completion",
            completion);
        HttpResponseMessage completeResponse = await client.SendAsync(completeRequest);

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        DoctorConsultationResponse completed = await ReadConsultationAsync(completeResponse);
        Assert.Equal(nameof(ConsultationStatus.Completed), completed.Status);
        Assert.Equal(nameof(AppointmentStatus.Completed), completed.Appointment.Status);
        Assert.NotNull(completed.CompletedAtUtc);

        await using ApplicationDbContext context = database.CreateContext();
        Appointment persistedAppointment = await context.Appointments
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == appointment.Id);
        Assert.Equal(AppointmentStatus.Completed, persistedAppointment.Status);
        Assert.Equal(
            1,
            await context.AuditEvents.CountAsync(auditEvent =>
                auditEvent.Action == "ConsultationStarted" &&
                auditEvent.AffectedEntityId == started.Id));
        Assert.Equal(
            1,
            await context.AuditEvents.CountAsync(auditEvent =>
                auditEvent.Action == "ConsultationCompleted" &&
                auditEvent.AffectedEntityId == started.Id));
        Assert.DoesNotContain(
            await context.AuditEvents
                .Where(auditEvent => auditEvent.AffectedEntityId == started.Id)
                .Select(auditEvent => auditEvent.MetadataJson)
                .ToArrayAsync(),
            metadata => metadata?.Contains("Private synthetic notes", StringComparison.Ordinal) ==
                true);
    }

    [Fact]
    public async Task AnotherDoctorCannotOpenAnAssignedConsultation()
    {
        Appointment appointment = await CreateAppointmentAsync(
            AuthTestIdentities.DoctorSubject,
            new DateTimeOffset(2030, 3, 5, 16, 0, 0, TimeSpan.Zero),
            "Ownership test");
        DoctorConsultationResponse consultation = await StartConsultationAsync(appointment);

        using HttpRequestMessage request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/v1/consultations/{consultation.Id}",
            AuthTestIdentities.OtherDoctorSubject,
            ApplicationRoles.Doctor);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StaleDraftAndIncompleteCompletionReturnExpectedProblems()
    {
        Appointment appointment = await CreateAppointmentAsync(
            AuthTestIdentities.DoctorSubject,
            new DateTimeOffset(2030, 3, 6, 16, 0, 0, TimeSpan.Zero),
            "Conflict test");
        DoctorConsultationResponse consultation = await StartConsultationAsync(appointment);

        using HttpRequestMessage firstSave = CreateDoctorRequest(
            HttpMethod.Put,
            $"/api/v1/consultations/{consultation.Id}",
            new SaveConsultationDraftRequest(
                "First update",
                null,
                null,
                null,
                consultation.Version));
        HttpResponseMessage firstSaveResponse = await client.SendAsync(firstSave);
        Assert.Equal(HttpStatusCode.OK, firstSaveResponse.StatusCode);

        using HttpRequestMessage staleSave = CreateDoctorRequest(
            HttpMethod.Put,
            $"/api/v1/consultations/{consultation.Id}",
            new SaveConsultationDraftRequest(
                "Stale update",
                null,
                null,
                null,
                consultation.Version));
        HttpResponseMessage staleResponse = await client.SendAsync(staleSave);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        string staleProblem = await staleResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"errorCode\":\"consultation_changed\"", staleProblem);
        Assert.Contains("\"traceId\"", staleProblem);

        DoctorConsultationResponse current = await ReadConsultationAsync(firstSaveResponse);
        using HttpRequestMessage incompleteCompletion = CreateDoctorRequest(
            HttpMethod.Post,
            $"/api/v1/consultations/{consultation.Id}/completion",
            new CompleteConsultationRequest(
                current.Outcome,
                null,
                null,
                null,
                current.Version));
        HttpResponseMessage incompleteResponse = await client.SendAsync(incompleteCompletion);
        Assert.Equal(HttpStatusCode.BadRequest, incompleteResponse.StatusCode);
        string incompleteProblem = await incompleteResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"errorCode\":\"incomplete_consultation\"", incompleteProblem);
        Assert.Contains("\"traceId\"", incompleteProblem);
    }

    [Fact]
    public async Task TwoConcurrentStartsCreateExactlyOneConsultation()
    {
        Appointment appointment = await CreateAppointmentAsync(
            AuthTestIdentities.DoctorSubject,
            new DateTimeOffset(2030, 3, 7, 16, 0, 0, TimeSpan.Zero),
            "Concurrent start test");

        Task<HttpResponseMessage>[] requests = Enumerable.Range(0, 2)
            .Select(_ => SendStartAsync(appointment))
            .ToArray();
        HttpResponseMessage[] responses = await Task.WhenAll(requests);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        await using ApplicationDbContext context = database.CreateContext();
        Assert.Equal(
            1,
            await context.Consultations.CountAsync(candidate =>
                candidate.AppointmentId == appointment.Id));
        Assert.Equal(
            AppointmentStatus.InProgress,
            await context.Appointments
                .Where(candidate => candidate.Id == appointment.Id)
                .Select(candidate => candidate.Status)
                .SingleAsync());

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private async Task<HttpResponseMessage> SendStartAsync(Appointment appointment)
    {
        using HttpRequestMessage request = CreateDoctorRequest(
            HttpMethod.Post,
            $"/api/v1/appointments/{appointment.Id}/consultations",
            new StartConsultationRequest(appointment.Version));
        return await client.SendAsync(request);
    }

    private async Task<DoctorConsultationResponse> StartConsultationAsync(
        Appointment appointment)
    {
        using HttpRequestMessage request = CreateDoctorRequest(
            HttpMethod.Post,
            $"/api/v1/appointments/{appointment.Id}/consultations",
            new StartConsultationRequest(appointment.Version));
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadConsultationAsync(response);
    }

    private async Task<Appointment> CreateAppointmentAsync(
        string doctorSubject,
        DateTimeOffset startsAtUtc,
        string reason)
    {
        await using ApplicationDbContext context = database.CreateContext();
        long clinicianProfileId = await context.ClinicianProfiles
            .Where(profile => profile.UserProfile.Auth0Subject == doctorSubject)
            .Select(profile => profile.Id)
            .SingleAsync();
        long patientProfileId = await context.PatientProfiles
            .Where(profile =>
                profile.UserProfile.Auth0Subject == AuthTestIdentities.PatientSubject)
            .Select(profile => profile.Id)
            .SingleAsync();

        AvailabilitySlot slot = new()
        {
            ClinicianProfileId = clinicianProfileId,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = startsAtUtc.AddMinutes(45),
            CreatedAtUtc = AuthTestClock.UtcNow,
        };
        context.AvailabilitySlots.Add(slot);
        await context.SaveChangesAsync();

        Appointment appointment = new()
        {
            PatientProfileId = patientProfileId,
            AvailabilitySlotId = slot.Id,
            Reason = reason,
            Status = AppointmentStatus.Scheduled,
            CreatedAtUtc = AuthTestClock.UtcNow,
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return appointment;
    }

    private HttpRequestMessage CreateDoctorRequest(
        HttpMethod method,
        string path,
        object? body = null) =>
        CreateAuthenticatedRequest(
            method,
            path,
            AuthTestIdentities.DoctorSubject,
            ApplicationRoles.Doctor,
            body);

    private HttpRequestMessage CreateAuthenticatedRequest(
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

    private static async Task<DoctorConsultationResponse> ReadConsultationAsync(
        HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<DoctorConsultationResponse>()
        ?? throw new InvalidOperationException("The consultation response was missing.");
}
