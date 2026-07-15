using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class PharmacistProfileConfiguration : IEntityTypeConfiguration<PharmacistProfile>
{
    public void Configure(EntityTypeBuilder<PharmacistProfile> builder)
    {
        builder.ToTable("pharmacist_profile", table =>
        {
            table.HasCheckConstraint(
                "pharmacist_profile_staff_identifier_check",
                "length(btrim(staff_identifier)) > 0");
            table.HasCheckConstraint(
                "pharmacist_profile_pharmacy_name_check",
                "length(btrim(pharmacy_name)) > 0");
        });

        builder.HasKey(profile => profile.Id)
            .HasName("pharmacist_profile_pkey");

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(profile => profile.UserProfileId)
            .HasColumnName("user_profile_id");

        builder.Property(profile => profile.StaffIdentifier)
            .HasColumnName("staff_identifier")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(profile => profile.PharmacyName)
            .HasColumnName("pharmacy_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(profile => profile.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(profile => profile.UserProfile)
            .WithOne()
            .HasForeignKey<PharmacistProfile>(profile => profile.UserProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("pharmacist_profile_user_profile_id_fkey");

        builder.HasIndex(profile => profile.UserProfileId)
            .IsUnique()
            .HasDatabaseName("pharmacist_profile_user_profile_id_unique");

        builder.HasIndex(profile => profile.StaffIdentifier)
            .IsUnique()
            .HasDatabaseName("pharmacist_profile_staff_identifier_unique");
    }
}
