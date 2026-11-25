using Microsoft.AspNetCore.Identity;

namespace AspireBoot.Core.Identity.Domain;

/// <summary>
/// Aggregate root for a person with an account. Identity owns credentials, lockout and tokens;
/// business state changes go through intention-revealing methods on this type.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    private User()
    {
    }

    public DisplayName DisplayName { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static User Create(string email, DisplayName displayName, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(clock);

        return new User
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            CreatedAt = clock.GetUtcNow(),
        };
    }

    public void Rename(DisplayName displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        DisplayName = displayName;
    }
}