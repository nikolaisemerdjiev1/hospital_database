using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class PatientProfileConfiguration : IEntityTypeConfiguration<PatientProfile>
{
    public void Configure(EntityTypeBuilder<PatientProfile> builder)
    {
        builder.ToTable("patient_profile", table =>
        {
            table.HasCheckConstraint(
                "patient_profile_medical_record_number_check",
                "length(btrim(medical_record_number)) > 0");
        });

        builder.HasKey(profile => profile.Id)
            .HasName("patient_profile_pkey");

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(profile => profile.UserProfileId)
            .HasColumnName("user_profile_id");

        builder.Property(profile => profile.MedicalRecordNumber)
            .HasColumnName("medical_record_number")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(profile => profile.DateOfBirth)
            .HasColumnName("date_of_birth")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(profile => profile.AllergySummary)
            .HasColumnName("allergy_summary")
            .HasMaxLength(500);

        builder.Property(profile => profile.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(profile => profile.UserProfile)
            .WithOne()
            .HasForeignKey<PatientProfile>(profile => profile.UserProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("patient_profile_user_profile_id_fkey");

        builder.HasIndex(profile => profile.UserProfileId)
            .IsUnique()
            .HasDatabaseName("patient_profile_user_profile_id_unique");

        builder.HasIndex(profile => profile.MedicalRecordNumber)
            .IsUnique()
            .HasDatabaseName("patient_profile_medical_record_number_unique");
    }
}
