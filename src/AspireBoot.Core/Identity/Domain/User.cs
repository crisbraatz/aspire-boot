using AspireBoot.Core.Shared;

using Microsoft.AspNetCore.Identity;

namespace AspireBoot.Core.Identity.Domain;

/// <summary>
/// Aggregate root for a person with an account. Credentials are passkeys only (no passwords); Identity
/// owns lockout and tokens. Business state changes go through intention-revealing methods on this type.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    public static readonly Failure LastPasskey = new("Identity.LastPasskey", "The last passkey of an account cannot be removed.");

    private User()
    {
    }

    public DisplayName DisplayName { get; private set; } = null!;

    public UiCulture PreferredCulture { get; private set; } = UiCulture.Default;

    public DateTimeOffset CreatedAt { get; private set; }

    private string? avatarKey;

    /// <summary>The current avatar object, or null when the user has none.</summary>
    public AvatarReference? Avatar => avatarKey is null ? null : AvatarReference.Restore(avatarKey, AvatarVersion);

    /// <summary>Grows on every upload so avatar URLs never repeat, even after a removal. Never reused.</summary>
    public int AvatarVersion { get; private set; }

    public static User Create(string email, DisplayName displayName, UiCulture culture, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(clock);

        return new User
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            PreferredCulture = culture,
            CreatedAt = clock.GetUtcNow(),
        };
    }

    public void Rename(DisplayName displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        DisplayName = displayName;
    }

    public void ChangePreferredCulture(UiCulture culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        PreferredCulture = culture;
    }

    /// <summary>Replaces the avatar with a newly stored object and returns the superseded reference, if any.</summary>
    public AvatarReference? ChangeAvatar(Guid objectId)
    {
        var superseded = Avatar;
        AvatarVersion++;
        avatarKey = AvatarReference.For(Id, objectId, AvatarVersion).Key;
        return superseded;
    }

    /// <summary>Removes the avatar. Returns the superseded reference, or null when there was none (idempotent).</summary>
    public AvatarReference? RemoveAvatar()
    {
        var superseded = Avatar;
        avatarKey = null;
        return superseded;
    }

    /// <summary>Passkeys are the only credential, so the last one cannot be removed or the account would be locked out.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Invariant is part of the aggregate's instance API; it is called on the user being changed.")]
    public Result EnsureCanRemovePasskey(int currentPasskeyCount)
    {
        return currentPasskeyCount <= 1
            ? Result.Fail(LastPasskey)
            : Result.Success();
    }
}