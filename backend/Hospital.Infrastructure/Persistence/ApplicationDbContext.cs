using Hospital.Core.Audit;
using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Persistence;
using Hospital.Core.Pharmacy;
using Hospital.Core.Prescriptions;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();

    public DbSet<ClinicianProfile> ClinicianProfiles => Set<ClinicianProfile>();

    public DbSet<PharmacistProfile> PharmacistProfiles => Set<PharmacistProfile>();

    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<Consultation> Consultations => Set<Consultation>();

    public DbSet<Medication> Medications => Set<Medication>();

    public DbSet<Prescription> Prescriptions => Set<Prescription>();

    public DbSet<Fulfillment> Fulfillments => Set<Fulfillment>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
