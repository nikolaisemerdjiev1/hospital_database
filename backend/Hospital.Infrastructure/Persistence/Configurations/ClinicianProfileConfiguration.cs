using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class ClinicianProfileConfiguration : IEntityTypeConfiguration<ClinicianProfile>
{
    public void Configure(EntityTypeBuilder<ClinicianProfile> builder)
    {
        builder.ToTable("clinician_profile", table =>
        {
            table.HasCheckConstraint(
                "clinician_profile_staff_identifier_check",
                "length(btrim(staff_identifier)) > 0");
            table.HasCheckConstraint(
                "clinician_profile_specialty_check",
                "length(btrim(specialty)) > 0");
        });

        builder.HasKey(profile => profile.Id)
            .HasName("clinician_profile_pkey");

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(profile => profile.UserProfileId)
            .HasColumnName("user_profile_id");

        builder.Property(profile => profile.StaffIdentifier)
            .HasColumnName("staff_identifier")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(profile => profile.Specialty)
            .HasColumnName("specialty")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(profile => profile.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(profile => profile.UserProfile)
            .WithOne()
            .HasForeignKey<ClinicianProfile>(profile => profile.UserProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("clinician_profile_user_profile_id_fkey");

        builder.HasIndex(profile => profile.UserProfileId)
            .IsUnique()
            .HasDatabaseName("clinician_profile_user_profile_id_unique");

        builder.HasIndex(profile => profile.StaffIdentifier)
            .IsUnique()
            .HasDatabaseName("clinician_profile_staff_identifier_unique");
    }
}
