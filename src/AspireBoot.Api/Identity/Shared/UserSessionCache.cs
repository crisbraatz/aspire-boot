using AspireBoot.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>The committed state of one session row, as the validator needs it.</summary>
internal sealed record SessionState(Guid UserId, bool Active, DateTimeOffset ExpiresAt, DateTimeOffset LastSeenAt);

/// <summary>Reads the committed session row straight from the database. Null when the row is gone.</summary>
internal interface IUserSessionSource
{
    Task<SessionState?> ReadAsync(Guid sessionId, CancellationToken cancellationToken);
}

internal sealed class DatabaseUserSessionSource(AppDbContext db) : IUserSessionSource
{
    public Task<SessionState?> ReadAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.UserSessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => new SessionState(session.UserId, session.RevokedAt == null, session.ExpiresAt, session.LastSeenAt))
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>The per-request view of a session record (ADR 0007).</summary>
internal interface IUserSessionCache
{
    /// <summary>The cached state (inactive when the row does not exist). May throw when the cache or the database fails.</summary>
    ValueTask<SessionState> GetAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>The state straight from the database, bypassing the cache.</summary>
    Task<SessionState> GetFromDatabaseAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Call after the transaction that changed the rows has committed. Never throws.</summary>
    Task SessionsChangedAsync(IReadOnlyCollection<Guid> sessionIds);

    /// <summary>Removes the entry so the next read reloads committed state. Never throws.</summary>
    Task InvalidateAsync(Guid sessionId);
}

/// <summary>
/// One entry per session in HybridCache, key <c>identity:session:{id}</c>, no tags, Redis only (same reasons and options as
/// <see cref="SecurityStampCache"/>). A missing row is cached as inactive.
/// </summary>
internal sealed partial class UserSessionCache(
    HybridCache cache,
    IUserSessionSource source,
    ILogger<UserSessionCache> logger) : IUserSessionCache
{
    internal static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(60);

    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = MaxLifetime,
        Flags = HybridCacheEntryFlags.DisableLocalCache,
    };

    internal static string Key(Guid sessionId) => $"identity:session:{sessionId:D}";

    public ValueTask<SessionState> GetAsync(Guid sessionId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            Key(sessionId),
            (source, sessionId),
            static async (state, token) => await state.source.ReadAsync(state.sessionId, token) ?? Missing,
            EntryOptions,
            tags: null,
            cancellationToken);

    public async Task<SessionState> GetFromDatabaseAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await source.ReadAsync(sessionId, cancellationToken) ?? Missing;

    public async Task SessionsChangedAsync(IReadOnlyCollection<Guid> sessionIds)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        foreach (var sessionId in sessionIds)
        {
            await OverwriteAsync(sessionId);
        }
    }

    public async Task InvalidateAsync(Guid sessionId)
    {
        try
        {
            await cache.RemoveAsync(Key(sessionId), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            InvalidationFailed(logger, sessionId, (int)MaxLifetime.TotalSeconds, exception.GetType().Name);
        }
    }

    private static SessionState Missing { get; } = new(Guid.Empty, false, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    private async Task OverwriteAsync(Guid sessionId)
    {
        // The post-commit work must finish even when the request was aborted, so it ignores the request token.
        var key = Key(sessionId);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                // Re-read the committed row, so the cache never receives a value that was not persisted.
                var state = await source.ReadAsync(sessionId, CancellationToken.None);
                if (state is null)
                {
                    await cache.SetAsync(key, Missing, EntryOptions, tags: null, CancellationToken.None);
                }
                else
                {
                    await cache.SetAsync(key, state, EntryOptions, tags: null, CancellationToken.None);
                }

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                OverwriteFailed(logger, sessionId, attempt, exception.GetType().Name);
            }
        }

        await InvalidateAsync(sessionId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Session cache overwrite failed for session {SessionId} (attempt {Attempt}): {ErrorType}.")]
    private static partial void OverwriteFailed(ILogger logger, Guid sessionId, int attempt, string errorType);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Session cache invalidation failed for session {SessionId}; the entry expires within {Seconds} s: {ErrorType}.")]
    private static partial void InvalidationFailed(ILogger logger, Guid sessionId, int seconds, string errorType);
}