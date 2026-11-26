using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Records that a session was seen, at most once per throttle window (ADR 0007).</summary>
internal interface IUserSessionHeartbeat
{
    /// <summary>True when this call wrote the row. The caller then clears the cache entry.</summary>
    Task<bool> TouchAsync(Guid sessionId, Guid userId, DateTimeOffset now, TimeSpan throttle, CancellationToken cancellationToken);
}

/// <summary>
/// A conditional <c>ExecuteUpdate</c>: it never touches a revoked or expired row, only matches the owner, and only one
/// request or instance wins a window. It bypasses the change tracker, so it is deliberately not audited (ADR 0006, 0007).
/// </summary>
internal sealed class DatabaseUserSessionHeartbeat(AppDbContext db, IOptionsMonitor<CookieAuthenticationOptions> cookieOptions) : IUserSessionHeartbeat
{
    public async Task<bool> TouchAsync(Guid sessionId, Guid userId, DateTimeOffset now, TimeSpan throttle, CancellationToken cancellationToken)
    {
        var expiresAt = now + cookieOptions.Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan;
        var staleBefore = now - throttle;

        var rows = await db.UserSessions
            .Where(session => session.Id == sessionId
                && session.UserId == userId
                && session.RevokedAt == null
                && session.ExpiresAt > now
                && session.LastSeenAt < staleBefore)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.LastSeenAt, now)
                    .SetProperty(session => session.ExpiresAt, expiresAt),
                cancellationToken);

        return rows == 1;
    }
}