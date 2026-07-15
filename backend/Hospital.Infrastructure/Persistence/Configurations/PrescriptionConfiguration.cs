using Hospital.Core.Prescriptions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("prescription", table =>
        {
            table.HasCheckConstraint(
                "prescription_rx_cui_snapshot_check",
                "length(btrim(rx_cui_snapshot)) > 0");
            table.HasCheckConstraint(
                "prescription_medication_name_snapshot_check",
                "length(btrim(medication_display_name_snapshot)) > 0");
            table.HasCheckConstraint(
                "prescription_dose_check",
                "length(btrim(dose)) > 0");
            table.HasCheckConstraint(
                "prescription_instructions_check",
                "length(btrim(instructions)) > 0");
            table.HasCheckConstraint(
                "prescription_quantity_check",
                "quantity > 0");
            table.HasCheckConstraint(
                "prescription_status_check",
                "status IN ('Issued', 'Cancelled')");
            table.HasCheckConstraint(
                "prescription_cancellation_check",
                "(status = 'Issued' AND cancelled_at_utc IS NULL) OR " +
                "(status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND " +
                "cancelled_at_utc >= issued_at_utc)");
        });

        builder.HasKey(prescription => prescription.Id)
            .HasName("prescription_pkey");

        builder.Property(prescription => prescription.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(prescription => prescription.ConsultationId)
            .HasColumnName("consultation_id");

        builder.Property(prescription => prescription.MedicationId)
            .HasColumnName("medication_id");

        builder.Property(prescription => prescription.PrescriberClinicianProfileId)
            .HasColumnName("prescriber_clinician_profile_id");

        builder.Property(prescription => prescription.PatientProfileId)
            .HasColumnName("patient_profile_id");

        builder.Property(prescription => prescription.RxCuiSnapshot)
            .HasColumnName("rx_cui_snapshot")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(prescription => prescription.MedicationDisplayNameSnapshot)
            .HasColumnName("medication_display_name_snapshot")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(prescription => prescription.Dose)
            .HasColumnName("dose")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(prescription => prescription.Instructions)
            .HasColumnName("instructions")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(prescription => prescription.Quantity)
            .HasColumnName("quantity");

        builder.Property(prescription => prescription.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(prescription => prescription.IssuedAtUtc)
            .HasColumnName("issued_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(prescription => prescription.CancelledAtUtc)
            .HasColumnName("cancelled_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(prescription => prescription.Version)
            .IsRowVersion();

        builder.HasOne(prescription => prescription.Consultation)
            .WithMany()
            .HasForeignKey(prescription => prescription.ConsultationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("prescription_consultation_id_fkey");

        builder.HasOne(prescription => prescription.Medication)
            .WithMany()
            .HasForeignKey(prescription => prescription.MedicationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("prescription_medication_id_fkey");

        builder.HasOne(prescription => prescription.PrescriberClinicianProfile)
            .WithMany()
            .HasForeignKey(prescription => prescription.PrescriberClinicianProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("prescription_prescriber_clinician_profile_id_fkey");

        builder.HasOne(prescription => prescription.PatientProfile)
            .WithMany()
            .HasForeignKey(prescription => prescription.PatientProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("prescription_patient_profile_id_fkey");

        builder.HasIndex(prescription => prescription.ConsultationId)
            .HasDatabaseName("prescription_consultation_id_idx");

        builder.HasIndex(prescription => prescription.MedicationId)
            .HasDatabaseName("prescription_medication_id_idx");

        builder.HasIndex(prescription => prescription.PrescriberClinicianProfileId)
            .HasDatabaseName("prescription_prescriber_clinician_profile_id_idx");

        builder.HasIndex(prescription => prescription.PatientProfileId)
            .HasDatabaseName("prescription_patient_profile_id_idx");
    }
}
