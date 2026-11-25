using Microsoft.AspNetCore.Identity;

namespace AspireBoot.Core.Identity.Domain;

/// <summary>An RBAC role. The built-in roles are listed in <see cref="Roles"/>.</summary>
public sealed class Role : IdentityRole<Guid>
{
    private Role()
    {
    }

    public static Role Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Role { Id = Guid.CreateVersion7(), Name = name, NormalizedName = name.ToUpperInvariant() };
    }
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
}