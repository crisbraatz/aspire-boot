using AspireBoot.Core.Identity.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AspireBoot.Core.Persistence.Configurations;

/// <summary>Seeds the two built-in roles with fixed ids, so no request ever has to create a role.</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder) =>
        builder.HasData(
            new
            {
                Id = Roles.AdminId,
                Name = Roles.Admin,
                NormalizedName = Roles.Admin.ToUpperInvariant(),
                ConcurrencyStamp = Roles.AdminConcurrencyStamp,
            },
            new
            {
                Id = Roles.UserId,
                Name = Roles.User,
                NormalizedName = Roles.User.ToUpperInvariant(),
                ConcurrencyStamp = Roles.UserConcurrencyStamp,
            });
}