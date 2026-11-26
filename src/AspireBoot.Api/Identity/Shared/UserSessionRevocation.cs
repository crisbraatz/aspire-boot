using AspireBoot.Core.Persistence;

using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Revokes session rows inside the caller's transaction (ADR 0007). The caller saves, then calls <c>SessionsChangedAsync</c>.</summary>
internal static class UserSessionRevocation
{
    /// <summary>Loads the account's active rows (tracked, so the change is audited), revokes them and returns their ids. Does not save.</summary>
    public static async Task<IReadOnlyCollection<Guid>> RevokeAsync(
        AppDbContext db, Guid userId, Guid? exceptSessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var sessions = await db.UserSessions
            .Where(session => session.UserId == userId
                && session.RevokedAt == null
                && session.ExpiresAt > now
                && session.Id != exceptSessionId)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(now);
        }

        return sessions.ConvertAll(session => session.Id);
    }
}