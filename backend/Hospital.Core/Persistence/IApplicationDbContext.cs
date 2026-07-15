using Hospital.Core.Audit;
using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Pharmacy;
using Hospital.Core.Prescriptions;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Persistence;

public interface IApplicationDbContext
{
    DbSet<UserProfile> UserProfiles { get; }

    DbSet<PatientProfile> PatientProfiles { get; }

    DbSet<ClinicianProfile> ClinicianProfiles { get; }

    DbSet<PharmacistProfile> PharmacistProfiles { get; }

    DbSet<AvailabilitySlot> AvailabilitySlots { get; }

    DbSet<Appointment> Appointments { get; }

    DbSet<Consultation> Consultations { get; }

    DbSet<Medication> Medications { get; }

    DbSet<Prescription> Prescriptions { get; }

    DbSet<Fulfillment> Fulfillments { get; }

    DbSet<AuditEvent> AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
