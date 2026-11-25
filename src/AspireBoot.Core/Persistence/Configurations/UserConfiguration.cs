using AspireBoot.Core.Identity.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireBoot.Core.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.DisplayName)
            .HasConversion(name => name.Value, value => DisplayName.Create(value).Value)
            .HasMaxLength(DisplayName.MaxLength)
            .IsRequired();

        builder.Property(user => user.PreferredCulture)
            .HasConversion(culture => culture.Value, value => UiCulture.Create(value).Value)
            .HasMaxLength(10)
            .HasDefaultValue(UiCulture.Default)
            .IsRequired();

        builder.Property(user => user.CreatedAt).IsRequired();
        builder.HasIndex(user => user.CreatedAt);

        // Avatar is derived from the stored key and AvatarVersion, so the key is persisted through its backing field.
        builder.Ignore(user => user.Avatar);

        builder.Property<string?>("avatarKey")
            .HasField("avatarKey")
            .HasColumnName("avatar_key")
            .HasMaxLength(200);

        builder.Property(user => user.AvatarVersion)
            .HasColumnName("avatar_version")
            .HasDefaultValue(0)
            .IsRequired();

        // PostgreSQL xmin is a system column: no DDL, no domain property. It detects concurrent updates.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .HasColumnName("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}