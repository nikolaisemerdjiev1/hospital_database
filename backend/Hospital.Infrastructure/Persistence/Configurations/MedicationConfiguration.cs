using Hospital.Core.Medications;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> builder)
    {
        builder.ToTable("medication", table =>
        {
            table.HasCheckConstraint(
                "medication_rx_cui_check",
                "length(btrim(rx_cui)) > 0");
            table.HasCheckConstraint(
                "medication_display_name_check",
                "length(btrim(display_name)) > 0");
            table.HasCheckConstraint(
                "medication_source_check",
                "source IN ('RxNorm', 'SeededFallback')");
        });

        builder.HasKey(medication => medication.Id)
            .HasName("medication_pkey");

        builder.Property(medication => medication.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(medication => medication.RxCui)
            .HasColumnName("rx_cui")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(medication => medication.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(medication => medication.Strength)
            .HasColumnName("strength")
            .HasMaxLength(100);

        builder.Property(medication => medication.DoseForm)
            .HasColumnName("dose_form")
            .HasMaxLength(100);

        builder.Property(medication => medication.Classification)
            .HasColumnName("classification")
            .HasMaxLength(100);

        builder.Property(medication => medication.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(medication => medication.LastVerifiedAtUtc)
            .HasColumnName("last_verified_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(medication => medication.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(medication => medication.RxCui)
            .IsUnique()
            .HasDatabaseName("medication_rx_cui_unique");
    }
}
