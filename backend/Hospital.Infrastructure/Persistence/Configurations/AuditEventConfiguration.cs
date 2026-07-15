using Hospital.Core.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hospital.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_event", table =>
        {
            table.HasCheckConstraint(
                "audit_event_action_check",
                "length(btrim(action)) > 0");
            table.HasCheckConstraint(
                "audit_event_affected_entity_type_check",
                "length(btrim(affected_entity_type)) > 0");
            table.HasCheckConstraint(
                "audit_event_metadata_size_check",
                "metadata_json IS NULL OR length(metadata_json::text) <= 4000");
        });

        builder.HasKey(auditEvent => auditEvent.Id)
            .HasName("audit_event_pkey");

        builder.Property(auditEvent => auditEvent.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(auditEvent => auditEvent.ActorUserProfileId)
            .HasColumnName("actor_user_profile_id");

        builder.Property(auditEvent => auditEvent.Action)
            .HasColumnName("action")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(auditEvent => auditEvent.AffectedEntityType)
            .HasColumnName("affected_entity_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(auditEvent => auditEvent.AffectedEntityId)
            .HasColumnName("affected_entity_id");

        builder.Property(auditEvent => auditEvent.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(auditEvent => auditEvent.TraceId)
            .HasColumnName("trace_id")
            .HasMaxLength(64);

        builder.Property(auditEvent => auditEvent.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb");

        builder.HasOne(auditEvent => auditEvent.ActorUserProfile)
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.ActorUserProfileId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("audit_event_actor_user_profile_id_fkey");

        builder.HasIndex(auditEvent => new
        {
            auditEvent.ActorUserProfileId,
            auditEvent.OccurredAtUtc,
        })
            .HasDatabaseName("audit_event_actor_occurred_at_utc_idx");

        builder.HasIndex(auditEvent => new
        {
            auditEvent.AffectedEntityType,
            auditEvent.AffectedEntityId,
            auditEvent.OccurredAtUtc,
        })
            .HasDatabaseName("audit_event_affected_entity_occurred_at_utc_idx");
    }
}
