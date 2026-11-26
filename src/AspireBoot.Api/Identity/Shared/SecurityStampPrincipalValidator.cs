using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// Checks, on every request, that the cookie's security stamp is still the account's current stamp (ADR 0005). It replaces
/// Identity's interval-based <c>SecurityStampValidator</c>. Fail safe: when the cache fails the database is read directly,
/// and when the database fails too the principal is rejected, never accepted.
/// </summary>
internal sealed partial class SecurityStampPrincipalValidator(
    ISecurityStampCache cache,
    IUserSessionCache sessions,
    IUserSessionHeartbeat heartbeat,
    IOptions<IdentityOptions> identityOptions,
    IOptions<UserSessionOptions> sessionOptions,
    TimeProvider clock,
    ILogger<SecurityStampPrincipalValidator> logger)
{
    /// <summary>Cookie event handler: rejects the principal and ends the cookie session when the stamp does not match.</summary>
    public static async Task OnValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var validator = context.HttpContext.RequestServices.GetRequiredService<SecurityStampPrincipalValidator>();
        if (await validator.IsValidAsync(context.Principal, context.HttpContext.RequestAborted))
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    public async Task<bool> IsValidAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var options = identityOptions.Value.ClaimsIdentity;
        var rawUserId = principal?.FindFirstValue(options.UserIdClaimType);
        var cookieStamp = principal?.FindFirstValue(options.SecurityStampClaimType);

        if (!Guid.TryParse(rawUserId, out var userId)
            || string.IsNullOrEmpty(cookieStamp)
            || !Guid.TryParse(principal?.FindFirstValue(SessionClaims.SessionId), out var sessionId))
        {
            MissingClaims(logger);
            return false;
        }

        // Sequential on purpose: both database fallbacks share the request's scoped AppDbContext, which does not allow
        // concurrent operations. Each lookup keeps its own fail-safe fallback.
        var current = await LookupStampAsync(userId, cancellationToken);
        var state = await LookupSessionAsync(sessionId, cancellationToken);

        if (string.IsNullOrEmpty(current) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current), Encoding.UTF8.GetBytes(cookieStamp)))
        {
            StampRejected(logger, userId);
            return false;
        }

        var now = clock.GetUtcNow();
        if (state is null || state.UserId != userId || !state.Active || state.ExpiresAt <= now)
        {
            SessionRejected(logger, userId);
            return false;
        }

        var throttle = sessionOptions.Value.LastSeenThrottle;
        if (now - state.LastSeenAt >= throttle)
        {
            await TouchAsync(sessionId, userId, now, throttle, cancellationToken);
        }

        return true;
    }

    private async Task TouchAsync(Guid sessionId, Guid userId, DateTimeOffset now, TimeSpan throttle, CancellationToken cancellationToken)
    {
        try
        {
            // Only the request that wins the conditional update clears the entry, so the next read reloads committed state.
            // The entry is never written from in-memory state: a revocation committed in between would be overwritten.
            if (await heartbeat.TouchAsync(sessionId, userId, now, throttle, cancellationToken))
            {
                await sessions.InvalidateAsync(sessionId);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HeartbeatFailed(logger, sessionId, exception.GetType().Name);
        }
    }

    private async Task<string?> LookupStampAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetStampAsync(userId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CacheFailed(logger, exception.GetType().Name);
            try
            {
                return await cache.GetStampFromDatabaseAsync(userId, cancellationToken);
            }
            catch (Exception databaseException) when (databaseException is not OperationCanceledException)
            {
                DatabaseFailed(logger, databaseException.GetType().Name);
                return null;
            }
        }
    }

    /// <summary>The session state, or null when both the cache and the database failed (the caller rejects).</summary>
    private async Task<SessionState?> LookupSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            return await sessions.GetAsync(sessionId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SessionCacheFailed(logger, exception.GetType().Name);
            try
            {
                return await sessions.GetFromDatabaseAsync(sessionId, cancellationToken);
            }
            catch (Exception databaseException) when (databaseException is not OperationCanceledException)
            {
                SessionDatabaseFailed(logger, databaseException.GetType().Name);
                return null;
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Session rejected: the cookie has no user id, security stamp or session id.")]
    private static partial void MissingClaims(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Session rejected for user {UserId}: the security stamp changed or the account is gone.")]
    private static partial void StampRejected(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Security stamp cache failed ({ErrorType}); reading the database directly.")]
    private static partial void CacheFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Security stamp lookup failed in the database ({ErrorType}); session rejected.")]
    private static partial void DatabaseFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Session rejected for user {UserId}: the session record is revoked, expired, missing or not theirs.")]
    private static partial void SessionRejected(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Session cache failed ({ErrorType}); reading the database directly.")]
    private static partial void SessionCacheFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Session lookup failed in the database ({ErrorType}); session rejected.")]
    private static partial void SessionDatabaseFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Last-seen update failed for session {SessionId} ({ErrorType}); the session stays valid.")]
    private static partial void HeartbeatFailed(ILogger logger, Guid sessionId, string errorType);
}