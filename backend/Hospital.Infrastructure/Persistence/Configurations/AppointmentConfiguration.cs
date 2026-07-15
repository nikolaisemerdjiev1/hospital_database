using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointment", table =>
        {
            table.HasCheckConstraint(
                "appointment_reason_check",
                "length(btrim(reason)) > 0");
            table.HasCheckConstraint(
                "appointment_status_check",
                "status IN ('Scheduled', 'InProgress', 'Completed', 'Cancelled', 'NoShow')");
            table.HasCheckConstraint(
                "appointment_cancellation_check",
                "(status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND " +
                "cancelled_at_utc >= created_at_utc) OR " +
                "(status <> 'Cancelled' AND cancelled_at_utc IS NULL AND cancellation_reason IS NULL)");
        });

        builder.HasKey(appointment => appointment.Id)
            .HasName("appointment_pkey");

        builder.Property(appointment => appointment.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(appointment => appointment.PatientProfileId)
            .HasColumnName("patient_profile_id");

        builder.Property(appointment => appointment.AvailabilitySlotId)
            .HasColumnName("availability_slot_id");

        builder.Property(appointment => appointment.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(appointment => appointment.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(appointment => appointment.CancelledAtUtc)
            .HasColumnName("cancelled_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(appointment => appointment.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        builder.Property(appointment => appointment.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(appointment => appointment.Version)
            .IsRowVersion();

        builder.HasOne(appointment => appointment.PatientProfile)
            .WithMany()
            .HasForeignKey(appointment => appointment.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("appointment_patient_profile_id_fkey");

        builder.HasOne(appointment => appointment.AvailabilitySlot)
            .WithMany()
            .HasForeignKey(appointment => appointment.AvailabilitySlotId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("appointment_availability_slot_id_fkey");

        builder.HasIndex(appointment => new
        {
            appointment.PatientProfileId,
            appointment.Status,
        })
            .HasDatabaseName("appointment_patient_profile_status_idx");

        builder.HasIndex(appointment => new
        {
            appointment.AvailabilitySlotId,
            appointment.Status,
        })
            .HasDatabaseName("appointment_availability_slot_status_idx");

        builder.HasIndex(appointment => appointment.AvailabilitySlotId)
            .IsUnique()
            .HasFilter("status <> 'Cancelled'")
            .HasDatabaseName("appointment_active_slot_unique");
    }
}
