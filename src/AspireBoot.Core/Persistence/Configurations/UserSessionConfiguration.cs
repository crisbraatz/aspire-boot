using AspireBoot.Core.Identity.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireBoot.Core.Persistence.Configurations;

/// <summary>
/// Maps server-side session records. There is deliberately no xmin concurrency token (ADR 0007): revoke is idempotent
/// and the heartbeat excludes revoked rows, so last writer wins safely.
/// </summary>
internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        builder.Property(session => session.UserId).IsRequired();
        builder.Property(session => session.CreatedAt).IsRequired();
        builder.Property(session => session.LastSeenAt).IsRequired();
        builder.Property(session => session.ExpiresAt).IsRequired();
        builder.Property(session => session.RevokedAt);
        builder.Property(session => session.IsPersistent).IsRequired();
        builder.Property(session => session.UserAgent).HasMaxLength(64).IsRequired();
        builder.Property(session => session.IpMasked).HasMaxLength(64).IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(session => new { session.UserId, session.RevokedAt, session.ExpiresAt });
        builder.HasIndex(session => session.ExpiresAt);
        builder.HasIndex(session => session.RevokedAt);
    }
}