using Hospital.Core.Consultations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class ConsultationConfiguration : IEntityTypeConfiguration<Consultation>
{
    public void Configure(EntityTypeBuilder<Consultation> builder)
    {
        builder.ToTable("consultation", table =>
        {
            table.HasCheckConstraint(
                "consultation_status_check",
                "status IN ('Draft', 'Completed')");
            table.HasCheckConstraint(
                "consultation_completion_check",
                "(status = 'Draft' AND completed_at_utc IS NULL) OR " +
                "(status = 'Completed' AND completed_at_utc IS NOT NULL AND " +
                "completed_at_utc >= started_at_utc AND outcome IS NOT NULL AND " +
                "length(btrim(outcome)) > 0 AND clinical_notes IS NOT NULL AND " +
                "length(btrim(clinical_notes)) > 0 AND patient_summary IS NOT NULL AND " +
                "length(btrim(patient_summary)) > 0 AND care_instructions IS NOT NULL AND " +
                "length(btrim(care_instructions)) > 0)");
        });

        builder.HasKey(consultation => consultation.Id)
            .HasName("consultation_pkey");

        builder.Property(consultation => consultation.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(consultation => consultation.AppointmentId)
            .HasColumnName("appointment_id");

        builder.Property(consultation => consultation.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(500);

        builder.Property(consultation => consultation.ClinicalNotes)
            .HasColumnName("clinical_notes")
            .HasMaxLength(4000);

        builder.Property(consultation => consultation.PatientSummary)
            .HasColumnName("patient_summary")
            .HasMaxLength(2000);

        builder.Property(consultation => consultation.CareInstructions)
            .HasColumnName("care_instructions")
            .HasMaxLength(2000);

        builder.Property(consultation => consultation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(consultation => consultation.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(consultation => consultation.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(consultation => consultation.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(consultation => consultation.Version)
            .IsRowVersion();

        builder.HasOne(consultation => consultation.Appointment)
            .WithOne()
            .HasForeignKey<Consultation>(consultation => consultation.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("consultation_appointment_id_fkey");

        builder.HasIndex(consultation => consultation.AppointmentId)
            .IsUnique()
            .HasDatabaseName("consultation_appointment_id_unique");
    }
}
