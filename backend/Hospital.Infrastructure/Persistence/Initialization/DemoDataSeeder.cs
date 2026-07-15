using Hospital.Core.Audit;
using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Pharmacy;
using Hospital.Core.Prescriptions;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Hospital.Infrastructure.Persistence.Initialization;

internal static class DemoDataSeeder
{
    private const string SeedMarkerAction = "DemoDataSeeded";
    private const string SeedMarkerEntityType = "DemoDataset";
    private const string SeedMarkerTraceId = "seed-dataset-v1";
    private const string SeedAdvisoryLockSql =
        "SELECT pg_advisory_xact_lock(4829531640271135)";

    private static readonly string[] PatientNames =
    [
        "Avery Brooks", "Jordan Lee", "Morgan Patel", "Taylor Nguyen",
        "Casey Rivera", "Riley Thompson", "Cameron Davis", "Quinn Martinez",
        "Parker Wilson", "Reese Anderson", "Skyler Thomas", "Emerson Clark",
        "Finley Lewis", "Hayden Walker", "Rowan Hall", "Dakota Allen",
        "Sage Young", "Alexis Hernandez", "Charlie King", "Kendall Wright",
        "Jamie Lopez", "Robin Hill", "Drew Scott", "Bailey Green",
        "Harper Adams", "Micah Baker", "Sam Nelson", "Kai Carter",
        "Remy Mitchell", "Jules Perez", "Blake Roberts", "Arden Turner",
        "Marley Phillips", "Shiloh Campbell", "Tatum Parker", "Elliot Evans",
    ];

    private static readonly string[] ClinicianNames =
    [
        "Dr. Maya Chen", "Dr. Theo Grant", "Dr. Nina Shah", "Dr. Owen Price",
        "Dr. Lena Ortiz", "Dr. Miles Bennett", "Dr. Priya Rao", "Dr. Eli Foster",
        "Dr. Zoe Kim", "Dr. Noah James",
    ];

    private static readonly string[] Specialties =
    [
        "Family Medicine", "Internal Medicine", "Cardiology", "Pediatrics",
        "Dermatology", "Neurology", "Endocrinology", "Pulmonology",
        "Geriatric Medicine", "Sports Medicine",
    ];

    private static readonly string[] PharmacistNames =
    [
        "Alex Morgan, PharmD", "Sydney Bell, PharmD", "Chris Reed, PharmD",
        "Dana Flores, PharmD",
    ];

    private static readonly string[] AppointmentReasons =
    [
        "Annual wellness visit", "Medication follow-up", "Persistent cough",
        "Blood pressure review", "Skin irritation", "Migraine follow-up",
        "Diabetes check-in", "Asthma symptom review", "Joint pain",
        "Sleep concerns", "Seasonal allergies", "Lab result discussion",
    ];

    public static async Task SeedAsync(
        ApplicationDbContext dbContext,
        DemoSeedOptions options,
        DateOnly anchorDate,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            SeedAdvisoryLockSql,
            cancellationToken);

        bool alreadySeeded = await dbContext.AuditEvents
            .AsNoTracking()
            .AnyAsync(
                auditEvent => auditEvent.Action == SeedMarkerAction &&
                    auditEvent.AffectedEntityType == SeedMarkerEntityType &&
                    auditEvent.TraceId == SeedMarkerTraceId,
                cancellationToken);

        if (alreadySeeded)
        {
            await VerifyConfiguredIdentitiesAsync(dbContext, options, cancellationToken);
            await VerifyDatasetShapeAsync(dbContext, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (await HasAnyDomainDataAsync(dbContext, cancellationToken))
        {
            throw new InvalidOperationException(
                "Demo data can only initialize an empty database. Existing data was not changed.");
        }

        DateTimeOffset anchor = new(
            anchorDate.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc));
        DateTimeOffset createdAt = anchor.AddDays(-120);

        List<UserProfile> patientUsers = CreatePatientUsers(options, createdAt);
        List<UserProfile> clinicianUsers = CreateClinicianUsers(options, createdAt);
        List<UserProfile> pharmacistUsers = CreatePharmacistUsers(options, createdAt);
        UserProfile administrator = new()
        {
            Auth0Subject = options.Subjects.Administrator,
            DisplayName = "Demo Administrator",
            ProfileType = ProfileType.Administrator,
            Status = AccountStatus.Active,
            CreatedAtUtc = createdAt,
        };

        List<UserProfile> allUsers =
        [
            .. patientUsers,
            .. clinicianUsers,
            .. pharmacistUsers,
            administrator,
        ];

        if (allUsers.Select(static user => user.Auth0Subject)
                .Distinct(StringComparer.Ordinal)
                .Count() != allUsers.Count)
        {
            throw new InvalidOperationException(
                "Configured demo subjects conflict with a deterministic synthetic subject.");
        }

        dbContext.UserProfiles.AddRange(allUsers);
        await dbContext.SaveChangesAsync(cancellationToken);

        List<PatientProfile> patients = CreatePatients(patientUsers, createdAt);
        List<ClinicianProfile> clinicians = CreateClinicians(clinicianUsers, createdAt);
        List<PharmacistProfile> pharmacists = CreatePharmacists(pharmacistUsers, createdAt);
        List<Medication> medications = CreateMedications(createdAt);

        dbContext.PatientProfiles.AddRange(patients);
        dbContext.ClinicianProfiles.AddRange(clinicians);
        dbContext.PharmacistProfiles.AddRange(pharmacists);
        dbContext.Medications.AddRange(medications);
        await dbContext.SaveChangesAsync(cancellationToken);

        List<AvailabilitySlot> appointmentSlots = CreateAppointmentSlots(clinicians, anchor);
        List<AvailabilitySlot> unbookedSlots = CreateUnbookedSlots(clinicians, anchor);
        dbContext.AvailabilitySlots.AddRange(appointmentSlots);
        dbContext.AvailabilitySlots.AddRange(unbookedSlots);
        await dbContext.SaveChangesAsync(cancellationToken);

        List<Appointment> appointments = CreateAppointments(patients, appointmentSlots);
        dbContext.Appointments.AddRange(appointments);
        await dbContext.SaveChangesAsync(cancellationToken);

        List<Consultation> consultations = CreateConsultations(appointments, appointmentSlots);
        dbContext.Consultations.AddRange(consultations);
        await dbContext.SaveChangesAsync(cancellationToken);

        List<Prescription> prescriptions = CreatePrescriptions(
            consultations,
            clinicians,
            patients,
            medications);
        dbContext.Prescriptions.AddRange(prescriptions);
        await dbContext.SaveChangesAsync(cancellationToken);

        List<Fulfillment> fulfillments = CreateFulfillments(prescriptions, pharmacists);
        dbContext.Fulfillments.AddRange(fulfillments);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.AuditEvents.AddRange(CreateAuditEvents(
            appointments,
            prescriptions,
            patients,
            appointmentSlots,
            clinicians,
            anchor));
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static List<UserProfile> CreatePatientUsers(
        DemoSeedOptions options,
        DateTimeOffset createdAt)
    {
        List<UserProfile> users = [];

        for (int index = 0; index < PatientNames.Length; index++)
        {
            users.Add(new UserProfile
            {
                Auth0Subject = index == 0
                    ? options.Subjects.Patient
                    : $"demo-seed|patient-{index + 1:000}",
                DisplayName = PatientNames[index],
                ProfileType = ProfileType.Patient,
                Status = AccountStatus.Active,
                CreatedAtUtc = createdAt,
            });
        }

        return users;
    }

    private static List<UserProfile> CreateClinicianUsers(
        DemoSeedOptions options,
        DateTimeOffset createdAt)
    {
        List<UserProfile> users = [];

        for (int index = 0; index < ClinicianNames.Length; index++)
        {
            users.Add(new UserProfile
            {
                Auth0Subject = index == 0
                    ? options.Subjects.Doctor
                    : $"demo-seed|doctor-{index + 1:000}",
                DisplayName = ClinicianNames[index],
                ProfileType = ProfileType.Doctor,
                Status = AccountStatus.Active,
                CreatedAtUtc = createdAt,
            });
        }

        return users;
    }

    private static List<UserProfile> CreatePharmacistUsers(
        DemoSeedOptions options,
        DateTimeOffset createdAt)
    {
        List<UserProfile> users = [];

        for (int index = 0; index < PharmacistNames.Length; index++)
        {
            users.Add(new UserProfile
            {
                Auth0Subject = index == 0
                    ? options.Subjects.Pharmacist
                    : $"demo-seed|pharmacist-{index + 1:000}",
                DisplayName = PharmacistNames[index],
                ProfileType = ProfileType.Pharmacist,
                Status = AccountStatus.Active,
                CreatedAtUtc = createdAt,
            });
        }

        return users;
    }

    private static List<PatientProfile> CreatePatients(
        List<UserProfile> patientUsers,
        DateTimeOffset createdAt)
    {
        List<PatientProfile> patients = [];

        for (int index = 0; index < patientUsers.Count; index++)
        {
            patients.Add(new PatientProfile
            {
                UserProfileId = patientUsers[index].Id,
                MedicalRecordNumber = $"MRN-{index + 1:00000}",
                DateOfBirth = new DateOnly(
                    1972 + (index % 31),
                    1 + (index % 12),
                    1 + (index % 28)),
                AllergySummary = index % 5 == 0 ? "Penicillin" : null,
                CreatedAtUtc = createdAt,
            });
        }

        return patients;
    }

    private static List<ClinicianProfile> CreateClinicians(
        List<UserProfile> clinicianUsers,
        DateTimeOffset createdAt)
    {
        List<ClinicianProfile> clinicians = [];

        for (int index = 0; index < clinicianUsers.Count; index++)
        {
            clinicians.Add(new ClinicianProfile
            {
                UserProfileId = clinicianUsers[index].Id,
                StaffIdentifier = $"DOC-{index + 1:000}",
                Specialty = Specialties[index],
                CreatedAtUtc = createdAt,
            });
        }

        return clinicians;
    }

    private static List<PharmacistProfile> CreatePharmacists(
        List<UserProfile> pharmacistUsers,
        DateTimeOffset createdAt)
    {
        List<PharmacistProfile> pharmacists = [];

        for (int index = 0; index < pharmacistUsers.Count; index++)
        {
            pharmacists.Add(new PharmacistProfile
            {
                UserProfileId = pharmacistUsers[index].Id,
                StaffIdentifier = $"PHR-{index + 1:000}",
                PharmacyName = "Harborview Community Pharmacy",
                CreatedAtUtc = createdAt,
            });
        }

        return pharmacists;
    }

    private static List<Medication> CreateMedications(DateTimeOffset createdAt) =>
    [
        CreateMedication("161", "Acetaminophen", "500 mg", "Oral tablet", createdAt),
        CreateMedication("5640", "Ibuprofen", "200 mg", "Oral tablet", createdAt),
        CreateMedication("723", "Amoxicillin", "500 mg", "Oral capsule", createdAt),
        CreateMedication("29046", "Lisinopril", "10 mg", "Oral tablet", createdAt),
        CreateMedication("6809", "Metformin", "500 mg", "Oral tablet", createdAt),
        CreateMedication("83367", "Atorvastatin", "20 mg", "Oral tablet", createdAt),
        CreateMedication("435", "Albuterol", "90 mcg", "Inhaler", createdAt),
        CreateMedication("7646", "Omeprazole", "20 mg", "Oral capsule", createdAt),
        CreateMedication("17767", "Amlodipine", "5 mg", "Oral tablet", createdAt),
        CreateMedication("36437", "Sertraline", "50 mg", "Oral tablet", createdAt),
        CreateMedication("52175", "Losartan", "50 mg", "Oral tablet", createdAt),
        CreateMedication("25480", "Gabapentin", "300 mg", "Oral capsule", createdAt),
    ];

    private static Medication CreateMedication(
        string rxCui,
        string name,
        string strength,
        string doseForm,
        DateTimeOffset createdAt) =>
        new()
        {
            RxCui = rxCui,
            DisplayName = name,
            Strength = strength,
            DoseForm = doseForm,
            Classification = "Demo reference medication",
            Source = MedicationSource.SeededFallback,
            LastVerifiedAtUtc = null,
            CreatedAtUtc = createdAt,
        };

    private static List<AvailabilitySlot> CreateAppointmentSlots(
        List<ClinicianProfile> clinicians,
        DateTimeOffset anchor)
    {
        List<AvailabilitySlot> slots = [];

        for (int index = 0; index < PatientNames.Length; index++)
        {
            int dayOffset = index switch
            {
                < 12 => -14 + index,
                < 18 => -8 + (index - 12),
                < 24 => -2 + (index - 18),
                < 34 => 1 + (index - 24),
                _ => 0,
            };
            DateTimeOffset startsAt = anchor
                .AddDays(dayOffset)
                .AddHours(index % 3);

            slots.Add(new AvailabilitySlot
            {
                ClinicianProfileId = clinicians[index % clinicians.Count].Id,
                StartsAtUtc = startsAt,
                EndsAtUtc = startsAt.AddMinutes(45),
                CreatedAtUtc = anchor.AddDays(-60),
            });
        }

        return slots;
    }

    private static List<AvailabilitySlot> CreateUnbookedSlots(
        List<ClinicianProfile> clinicians,
        DateTimeOffset anchor)
    {
        List<AvailabilitySlot> slots = [];

        for (int index = 0; index < 24; index++)
        {
            DateTimeOffset startsAt = anchor
                .AddDays(14 + (index / clinicians.Count))
                .AddHours(5 + (index % 2));

            slots.Add(new AvailabilitySlot
            {
                ClinicianProfileId = clinicians[index % clinicians.Count].Id,
                StartsAtUtc = startsAt,
                EndsAtUtc = startsAt.AddMinutes(45),
                CreatedAtUtc = anchor.AddDays(-30),
            });
        }

        return slots;
    }

    private static List<Appointment> CreateAppointments(
        List<PatientProfile> patients,
        List<AvailabilitySlot> slots)
    {
        List<Appointment> appointments = [];

        for (int index = 0; index < patients.Count; index++)
        {
            AppointmentStatus status = index switch
            {
                < 12 => AppointmentStatus.Completed,
                < 18 => AppointmentStatus.NoShow,
                < 24 => AppointmentStatus.Cancelled,
                < 34 => AppointmentStatus.Scheduled,
                _ => AppointmentStatus.InProgress,
            };
            DateTimeOffset? cancelledAt = status == AppointmentStatus.Cancelled
                ? slots[index].StartsAtUtc.AddDays(-2)
                : null;

            appointments.Add(new Appointment
            {
                PatientProfileId = patients[index].Id,
                AvailabilitySlotId = slots[index].Id,
                Reason = AppointmentReasons[index % AppointmentReasons.Length],
                Status = status,
                CancelledAtUtc = cancelledAt,
                CancellationReason = cancelledAt.HasValue ? "Schedule conflict" : null,
                CreatedAtUtc = slots[index].StartsAtUtc.AddDays(-21),
            });
        }

        return appointments;
    }

    private static List<Consultation> CreateConsultations(
        List<Appointment> appointments,
        List<AvailabilitySlot> slots)
    {
        List<Consultation> consultations = [];

        for (int index = 0; index < 12; index++)
        {
            DateTimeOffset startedAt = slots[index].StartsAtUtc;
            consultations.Add(new Consultation
            {
                AppointmentId = appointments[index].Id,
                Outcome = "Follow-up plan established",
                ClinicalNotes = "Synthetic clinical note for workflow demonstration only.",
                PatientSummary = "Visit completed and care plan reviewed.",
                CareInstructions = "Follow the care plan and schedule the recommended follow-up.",
                Status = ConsultationStatus.Completed,
                StartedAtUtc = startedAt,
                CompletedAtUtc = startedAt.AddMinutes(35),
                CreatedAtUtc = startedAt,
            });
        }

        for (int index = 34; index < 36; index++)
        {
            DateTimeOffset startedAt = slots[index].StartsAtUtc;
            consultations.Add(new Consultation
            {
                AppointmentId = appointments[index].Id,
                Outcome = null,
                ClinicalNotes = null,
                PatientSummary = null,
                CareInstructions = null,
                Status = ConsultationStatus.Draft,
                StartedAtUtc = startedAt,
                CompletedAtUtc = null,
                CreatedAtUtc = startedAt,
            });
        }

        return consultations;
    }

    private static List<Prescription> CreatePrescriptions(
        List<Consultation> consultations,
        List<ClinicianProfile> clinicians,
        List<PatientProfile> patients,
        List<Medication> medications)
    {
        List<Prescription> prescriptions = [];

        for (int index = 0; index < 9; index++)
        {
            Medication medication = medications[index % medications.Count];
            DateTimeOffset issuedAt = consultations[index].CompletedAtUtc!.Value;
            PrescriptionStatus status = index == 8
                ? PrescriptionStatus.Cancelled
                : PrescriptionStatus.Issued;

            prescriptions.Add(new Prescription
            {
                ConsultationId = consultations[index].Id,
                MedicationId = medication.Id,
                PrescriberClinicianProfileId = clinicians[index % clinicians.Count].Id,
                PatientProfileId = patients[index].Id,
                RxCuiSnapshot = medication.RxCui,
                MedicationDisplayNameSnapshot = medication.DisplayName,
                Dose = medication.Strength ?? "As directed",
                Instructions = "Take as directed in this synthetic demonstration.",
                Quantity = 30,
                Status = status,
                IssuedAtUtc = issuedAt,
                CancelledAtUtc = status == PrescriptionStatus.Cancelled
                    ? issuedAt.AddMinutes(30)
                    : null,
            });
        }

        return prescriptions;
    }

    private static List<Fulfillment> CreateFulfillments(
        List<Prescription> prescriptions,
        List<PharmacistProfile> pharmacists)
    {
        List<Fulfillment> fulfillments = [];

        for (int index = 0; index < prescriptions.Count; index++)
        {
            FulfillmentStatus status = index switch
            {
                0 or 7 => FulfillmentStatus.Pending,
                1 or 6 => FulfillmentStatus.InReview,
                2 or 5 => FulfillmentStatus.Ready,
                3 => FulfillmentStatus.Dispensed,
                _ => FulfillmentStatus.Cancelled,
            };
            DateTimeOffset createdAt = prescriptions[index].IssuedAtUtc.AddMinutes(5);
            bool assigned = status is FulfillmentStatus.InReview or
                FulfillmentStatus.Ready or FulfillmentStatus.Dispensed;
            DateTimeOffset? reviewStartedAt = assigned ? createdAt.AddMinutes(10) : null;
            DateTimeOffset? readyAt = status is FulfillmentStatus.Ready or
                FulfillmentStatus.Dispensed
                ? reviewStartedAt!.Value.AddMinutes(30)
                : null;

            fulfillments.Add(new Fulfillment
            {
                PrescriptionId = prescriptions[index].Id,
                AssignedPharmacistProfileId = assigned
                    ? pharmacists[index % pharmacists.Count].Id
                    : null,
                Status = status,
                CreatedAtUtc = createdAt,
                ReviewStartedAtUtc = reviewStartedAt,
                ReadyAtUtc = readyAt,
                DispensedAtUtc = status == FulfillmentStatus.Dispensed
                    ? readyAt!.Value.AddMinutes(20)
                    : null,
                CancelledAtUtc = status == FulfillmentStatus.Cancelled
                    ? createdAt.AddMinutes(25)
                    : null,
            });
        }

        return fulfillments;
    }

    private static List<AuditEvent> CreateAuditEvents(
        List<Appointment> appointments,
        List<Prescription> prescriptions,
        List<PatientProfile> patients,
        List<AvailabilitySlot> appointmentSlots,
        List<ClinicianProfile> clinicians,
        DateTimeOffset anchor)
    {
        List<AuditEvent> auditEvents = [];

        for (int index = 0; index < appointments.Count; index++)
        {
            Appointment appointment = appointments[index];
            bool clinicianAction = appointment.Status is
                AppointmentStatus.InProgress or
                AppointmentStatus.Completed or
                AppointmentStatus.NoShow;
            DateTimeOffset occurredAt = appointment.Status switch
            {
                AppointmentStatus.Cancelled => appointment.CancelledAtUtc!.Value,
                AppointmentStatus.InProgress => appointmentSlots[index].StartsAtUtc,
                AppointmentStatus.Completed or AppointmentStatus.NoShow =>
                    appointmentSlots[index].EndsAtUtc,
                _ => appointment.CreatedAtUtc,
            };

            auditEvents.Add(new AuditEvent
            {
                ActorUserProfileId = clinicianAction
                    ? clinicians[index % clinicians.Count].UserProfileId
                    : patients[index].UserProfileId,
                Action = $"Appointment{appointment.Status}",
                AffectedEntityType = nameof(Appointment),
                AffectedEntityId = appointment.Id,
                OccurredAtUtc = occurredAt,
                TraceId = $"seed-appointment-{index + 1:000}",
                MetadataJson = "{\"synthetic\":true,\"seedVersion\":\"v1\"}",
            });
        }

        for (int index = 0; index < prescriptions.Count; index++)
        {
            Prescription prescription = prescriptions[index];
            auditEvents.Add(new AuditEvent
            {
                ActorUserProfileId = clinicians[index % clinicians.Count].UserProfileId,
                Action = $"Prescription{prescription.Status}",
                AffectedEntityType = nameof(Prescription),
                AffectedEntityId = prescription.Id,
                OccurredAtUtc = prescription.CancelledAtUtc ?? prescription.IssuedAtUtc,
                TraceId = $"seed-prescription-{index + 1:000}",
                MetadataJson = "{\"synthetic\":true,\"seedVersion\":\"v1\"}",
            });
        }

        auditEvents.Add(new AuditEvent
        {
            ActorUserProfileId = null,
            Action = SeedMarkerAction,
            AffectedEntityType = SeedMarkerEntityType,
            AffectedEntityId = null,
            OccurredAtUtc = anchor,
            TraceId = SeedMarkerTraceId,
            MetadataJson = "{\"synthetic\":true,\"seedVersion\":\"v1\"}",
        });

        return auditEvents;
    }

    private static async Task VerifyConfiguredIdentitiesAsync(
        ApplicationDbContext dbContext,
        DemoSeedOptions options,
        CancellationToken cancellationToken)
    {
        (string Subject, ProfileType Type)[] expectedIdentities =
        [
            (options.Subjects.Patient, ProfileType.Patient),
            (options.Subjects.Doctor, ProfileType.Doctor),
            (options.Subjects.Pharmacist, ProfileType.Pharmacist),
            (options.Subjects.Administrator, ProfileType.Administrator),
        ];

        foreach ((string subject, ProfileType type) in expectedIdentities)
        {
            UserProfile? user = await dbContext.UserProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    profile => profile.Auth0Subject == subject,
                    cancellationToken);

            if (user is null || user.ProfileType != type || user.Status != AccountStatus.Active)
            {
                throw new InvalidOperationException(
                    $"The existing demo identity for {type} does not match the configured subject.");
            }

            int patientProfiles = await dbContext.PatientProfiles
                .CountAsync(profile => profile.UserProfileId == user.Id, cancellationToken);
            int clinicianProfiles = await dbContext.ClinicianProfiles
                .CountAsync(profile => profile.UserProfileId == user.Id, cancellationToken);
            int pharmacistProfiles = await dbContext.PharmacistProfiles
                .CountAsync(profile => profile.UserProfileId == user.Id, cancellationToken);

            bool hasExpectedSubtype = type switch
            {
                ProfileType.Patient => patientProfiles == 1 &&
                    clinicianProfiles == 0 && pharmacistProfiles == 0,
                ProfileType.Doctor => patientProfiles == 0 &&
                    clinicianProfiles == 1 && pharmacistProfiles == 0,
                ProfileType.Pharmacist => patientProfiles == 0 &&
                    clinicianProfiles == 0 && pharmacistProfiles == 1,
                ProfileType.Administrator => patientProfiles == 0 &&
                    clinicianProfiles == 0 && pharmacistProfiles == 0,
                _ => false,
            };

            if (!hasExpectedSubtype)
            {
                throw new InvalidOperationException(
                    $"The existing demo identity for {type} has an invalid local profile composition.");
            }
        }
    }

    private static async Task<bool> HasAnyDomainDataAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken) =>
        await dbContext.UserProfiles.AnyAsync(cancellationToken) ||
        await dbContext.PatientProfiles.AnyAsync(cancellationToken) ||
        await dbContext.ClinicianProfiles.AnyAsync(cancellationToken) ||
        await dbContext.PharmacistProfiles.AnyAsync(cancellationToken) ||
        await dbContext.AvailabilitySlots.AnyAsync(cancellationToken) ||
        await dbContext.Appointments.AnyAsync(cancellationToken) ||
        await dbContext.Consultations.AnyAsync(cancellationToken) ||
        await dbContext.Medications.AnyAsync(cancellationToken) ||
        await dbContext.Prescriptions.AnyAsync(cancellationToken) ||
        await dbContext.Fulfillments.AnyAsync(cancellationToken) ||
        await dbContext.AuditEvents.AnyAsync(cancellationToken);

    private static async Task VerifyDatasetShapeAsync(
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        bool hasExpectedShape =
            await dbContext.UserProfiles.CountAsync(cancellationToken) == 51 &&
            await dbContext.PatientProfiles.CountAsync(cancellationToken) == 36 &&
            await dbContext.ClinicianProfiles.CountAsync(cancellationToken) == 10 &&
            await dbContext.PharmacistProfiles.CountAsync(cancellationToken) == 4 &&
            await dbContext.AvailabilitySlots.CountAsync(cancellationToken) == 60 &&
            await dbContext.Appointments.CountAsync(cancellationToken) == 36 &&
            await dbContext.Consultations.CountAsync(cancellationToken) == 14 &&
            await dbContext.Medications.CountAsync(cancellationToken) == 12 &&
            await dbContext.Prescriptions.CountAsync(cancellationToken) == 9 &&
            await dbContext.Fulfillments.CountAsync(cancellationToken) == 9 &&
            await dbContext.AuditEvents.CountAsync(cancellationToken) == 46;

        if (!hasExpectedShape)
        {
            throw new InvalidOperationException(
                "The existing demo seed marker does not match a complete v1 dataset.");
        }
    }
}
