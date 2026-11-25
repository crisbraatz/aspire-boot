using AspireBoot.Core.Shared;

namespace AspireBoot.Core.Identity.Domain;

/// <summary>Role rules that span several users, so they are pure functions over the values the slice loads under the Admin row lock.</summary>
public static class RoleAssignment
{
    public static readonly Failure SelfDemotion = new("identity.selfDemotion", "You cannot remove your own Admin role.");
    public static readonly Failure LastAdmin = new("identity.lastAdmin", "The last Admin cannot be demoted.");
    public static readonly Failure InvalidRole = new("identity.invalidRole", "The role is not recognized.");

    /// <summary>Checks whether an actor may move a target user from <paramref name="currentRole"/> to <paramref name="newRole"/>. <paramref name="adminCount"/> counts every Admin, the target included.</summary>
    public static Result EnsureCanChangeRole(Guid actorId, Guid targetId, string currentRole, string newRole, int adminCount)
    {
        if (!Roles.IsKnown(newRole))
        {
            return Result.Fail(InvalidRole);
        }

        if (string.Equals(currentRole, newRole, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        if (!IsDemotionFromAdmin(currentRole, newRole))
        {
            return Result.Success();
        }

        if (actorId == targetId)
        {
            return Result.Fail(SelfDemotion);
        }

        if (adminCount <= 1)
        {
            return Result.Fail(LastAdmin);
        }

        return Result.Success();
    }

    /// <summary>True when the configured bootstrap email matches the confirmed user email (trimmed, case-insensitive) and the user is not yet an Admin.</summary>
    public static bool ShouldBootstrapAdmin(string? configuredEmail, string userEmail, bool emailConfirmed, bool alreadyAdmin)
    {
        if (string.IsNullOrWhiteSpace(configuredEmail) || !emailConfirmed || alreadyAdmin)
        {
            return false;
        }

        return string.Equals(configuredEmail.Trim(), userEmail.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDemotionFromAdmin(string currentRole, string newRole) =>
        string.Equals(currentRole, Roles.Admin, StringComparison.Ordinal)
        && string.Equals(newRole, Roles.User, StringComparison.Ordinal);
}