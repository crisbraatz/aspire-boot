using AspireBoot.Core.Persistence.Auditing;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireBoot.Core.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.ActorType).HasMaxLength(10).IsRequired();
        builder.Property(entry => entry.TableName).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.EntityType).HasMaxLength(256).IsRequired();
        builder.Property(entry => entry.EntityKey).HasMaxLength(512).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(10).IsRequired();
        builder.Property(entry => entry.Changes).HasColumnType("jsonb").IsRequired();
        builder.Property(entry => entry.TraceId).HasMaxLength(64);

        builder.HasIndex(entry => new { entry.ActorUserId, entry.OccurredAt });
        builder.HasIndex(entry => new { entry.EntityType, entry.EntityKey, entry.OccurredAt });
    }
}