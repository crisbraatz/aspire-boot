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

    /// <summary>Fixed ids of the seeded built-in roles (migration SeedRoles), so every database and every instance agrees on them.</summary>
    public static readonly Guid AdminId = new("0198a000-0000-7000-8000-000000000001");

    public static readonly Guid UserId = new("0198a000-0000-7000-8000-000000000002");

    /// <summary>Fixed concurrency stamps of the seeded rows, so the model snapshot and the migration never drift.</summary>
    public const string AdminConcurrencyStamp = "5d1f4a0e-0000-4000-8000-000000000001";

    public const string UserConcurrencyStamp = "5d1f4a0e-0000-4000-8000-000000000002";

    public static readonly IReadOnlyList<string> All = [Admin, User];

    public static bool IsKnown(string? name) => name is not null && All.Contains(name, StringComparer.Ordinal);
}