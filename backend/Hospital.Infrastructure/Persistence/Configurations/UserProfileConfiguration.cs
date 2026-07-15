using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profile", table =>
        {
            table.HasCheckConstraint(
                "user_profile_auth0_subject_check",
                "length(btrim(auth0_subject)) > 0");
            table.HasCheckConstraint(
                "user_profile_display_name_check",
                "length(btrim(display_name)) > 0");
            table.HasCheckConstraint(
                "user_profile_profile_type_check",
                "profile_type IN ('Patient', 'Doctor', 'Pharmacist', 'Administrator')");
            table.HasCheckConstraint(
                "user_profile_account_status_check",
                "account_status IN ('Active', 'Inactive')");
        });

        builder.HasKey(profile => profile.Id)
            .HasName("user_profile_pkey");

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(profile => profile.Auth0Subject)
            .HasColumnName("auth0_subject")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(profile => profile.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(profile => profile.ProfileType)
            .HasColumnName("profile_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(profile => profile.Status)
            .HasColumnName("account_status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(profile => profile.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(profile => profile.Auth0Subject)
            .IsUnique()
            .HasDatabaseName("user_profile_auth0_subject_unique");
    }
}
