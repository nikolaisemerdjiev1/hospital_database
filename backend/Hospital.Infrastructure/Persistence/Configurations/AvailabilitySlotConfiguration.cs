using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> builder)
    {
        builder.ToTable("availability_slot", table =>
        {
            table.HasCheckConstraint(
                "availability_slot_time_range_check",
                "ends_at_utc > starts_at_utc");
        });

        builder.HasKey(slot => slot.Id)
            .HasName("availability_slot_pkey");

        builder.Property(slot => slot.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(slot => slot.ClinicianProfileId)
            .HasColumnName("clinician_profile_id");

        builder.Property(slot => slot.StartsAtUtc)
            .HasColumnName("starts_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(slot => slot.EndsAtUtc)
            .HasColumnName("ends_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(slot => slot.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(slot => slot.Version)
            .IsRowVersion();

        builder.HasOne(slot => slot.ClinicianProfile)
            .WithMany()
            .HasForeignKey(slot => slot.ClinicianProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("availability_slot_clinician_profile_id_fkey");

        builder.HasIndex(slot => new
        {
            slot.ClinicianProfileId,
            slot.StartsAtUtc,
        })
            .IsUnique()
            .HasDatabaseName("availability_slot_clinician_start_unique");
    }
}
