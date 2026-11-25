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

        builder.Property(user => user.CreatedAt).IsRequired();
        builder.HasIndex(user => user.CreatedAt);
    }
}