using Hospital.Core.Pharmacy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class FulfillmentConfiguration : IEntityTypeConfiguration<Fulfillment>
{
    public void Configure(EntityTypeBuilder<Fulfillment> builder)
    {
        builder.ToTable("fulfillment", table =>
        {
            table.HasCheckConstraint(
                "fulfillment_status_check",
                "status IN ('Pending', 'InReview', 'Ready', 'Dispensed', 'Cancelled')");
            table.HasCheckConstraint(
                "fulfillment_assignment_check",
                "(assigned_pharmacist_profile_id IS NULL) = (review_started_at_utc IS NULL)");
            table.HasCheckConstraint(
                "fulfillment_state_check",
                "(status = 'Pending' AND assigned_pharmacist_profile_id IS NULL AND " +
                "review_started_at_utc IS NULL AND ready_at_utc IS NULL AND " +
                "dispensed_at_utc IS NULL AND cancelled_at_utc IS NULL) OR " +
                "(status = 'InReview' AND assigned_pharmacist_profile_id IS NOT NULL AND " +
                "review_started_at_utc IS NOT NULL AND ready_at_utc IS NULL AND " +
                "dispensed_at_utc IS NULL AND cancelled_at_utc IS NULL) OR " +
                "(status = 'Ready' AND assigned_pharmacist_profile_id IS NOT NULL AND " +
                "review_started_at_utc IS NOT NULL AND ready_at_utc IS NOT NULL AND " +
                "dispensed_at_utc IS NULL AND cancelled_at_utc IS NULL) OR " +
                "(status = 'Dispensed' AND assigned_pharmacist_profile_id IS NOT NULL AND " +
                "review_started_at_utc IS NOT NULL AND ready_at_utc IS NOT NULL AND " +
                "dispensed_at_utc IS NOT NULL AND cancelled_at_utc IS NULL) OR " +
                "(status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND dispensed_at_utc IS NULL)");
            table.HasCheckConstraint(
                "fulfillment_timestamp_order_check",
                "(review_started_at_utc IS NULL OR review_started_at_utc >= created_at_utc) AND " +
                "(ready_at_utc IS NULL OR (review_started_at_utc IS NOT NULL AND " +
                "ready_at_utc >= review_started_at_utc)) AND " +
                "(dispensed_at_utc IS NULL OR (ready_at_utc IS NOT NULL AND " +
                "dispensed_at_utc >= ready_at_utc)) AND " +
                "(cancelled_at_utc IS NULL OR " +
                "cancelled_at_utc >= COALESCE(ready_at_utc, review_started_at_utc, created_at_utc))");
        });

        builder.HasKey(fulfillment => fulfillment.Id)
            .HasName("fulfillment_pkey");

        builder.Property(fulfillment => fulfillment.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(fulfillment => fulfillment.PrescriptionId)
            .HasColumnName("prescription_id");

        builder.Property(fulfillment => fulfillment.AssignedPharmacistProfileId)
            .HasColumnName("assigned_pharmacist_profile_id");

        builder.Property(fulfillment => fulfillment.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(fulfillment => fulfillment.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(fulfillment => fulfillment.ReviewStartedAtUtc)
            .HasColumnName("review_started_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(fulfillment => fulfillment.ReadyAtUtc)
            .HasColumnName("ready_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(fulfillment => fulfillment.DispensedAtUtc)
            .HasColumnName("dispensed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(fulfillment => fulfillment.CancelledAtUtc)
            .HasColumnName("cancelled_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(fulfillment => fulfillment.Version)
            .IsRowVersion();

        builder.HasOne(fulfillment => fulfillment.Prescription)
            .WithOne()
            .HasForeignKey<Fulfillment>(fulfillment => fulfillment.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fulfillment_prescription_id_fkey");

        builder.HasOne(fulfillment => fulfillment.AssignedPharmacistProfile)
            .WithMany()
            .HasForeignKey(fulfillment => fulfillment.AssignedPharmacistProfileId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fulfillment_assigned_pharmacist_profile_id_fkey");

        builder.HasIndex(fulfillment => fulfillment.PrescriptionId)
            .IsUnique()
            .HasDatabaseName("fulfillment_prescription_id_unique");

        builder.HasIndex(fulfillment => fulfillment.AssignedPharmacistProfileId)
            .HasDatabaseName("fulfillment_assigned_pharmacist_profile_id_idx");

        builder.HasIndex(fulfillment => new
        {
            fulfillment.Status,
            fulfillment.CreatedAtUtc,
        })
            .HasDatabaseName("fulfillment_status_created_at_utc_idx");
    }
}
