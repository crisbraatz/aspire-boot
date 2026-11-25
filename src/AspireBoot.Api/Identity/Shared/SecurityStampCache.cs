using AspireBoot.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Reads the committed security stamp of an account straight from the database. Null when the account is gone.</summary>
internal interface ISecurityStampSource
{
    Task<string?> ReadAsync(Guid userId, CancellationToken cancellationToken);
}

internal sealed class DatabaseSecurityStampSource(AppDbContext db) : ISecurityStampSource
{
    public Task<string?> ReadAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>The per-request view of the current security stamp, used to revoke sessions at once (ADR 0005).</summary>
internal interface ISecurityStampCache
{
    /// <summary>The cached stamp (empty when the account does not exist). May throw when the cache or the database fails.</summary>
    ValueTask<string> GetStampAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The stamp straight from the database, bypassing the cache. Null when the account does not exist.</summary>
    Task<string?> GetStampFromDatabaseAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Call after the transaction that changed the stamp has committed. Never throws.</summary>
    Task StampChangedAsync(Guid userId);
}

/// <summary>
/// Security stamp per account in HybridCache, key <c>identity:stamp:{id}</c>, no tags. The local (L1) cache is disabled for
/// this entry because HybridCache does not propagate L1 invalidation across instances: Redis (L2) is the single source, so
/// a stamp change is seen by every API instance on its next request. The 60 s expiration is only a documented safety bound
/// for the case where both the overwrite and the removal fail after a commit.
/// </summary>
internal sealed partial class SecurityStampCache(
    HybridCache cache,
    ISecurityStampSource source,
    ILogger<SecurityStampCache> logger) : ISecurityStampCache
{
    /// <summary>Upper bound on how long a stale stamp can live if invalidation failed.</summary>
    internal static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(60);

    // Reads and writes must use the same options (flags and expiration).
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = MaxLifetime,
        Flags = HybridCacheEntryFlags.DisableLocalCache,
    };

    internal static string Key(Guid userId) => $"identity:stamp:{userId:D}";

    public ValueTask<string> GetStampAsync(Guid userId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            Key(userId),
            (source, userId),
            static async (state, token) => await state.source.ReadAsync(state.userId, token) ?? string.Empty,
            EntryOptions,
            tags: null,
            cancellationToken);

    public Task<string?> GetStampFromDatabaseAsync(Guid userId, CancellationToken cancellationToken) =>
        source.ReadAsync(userId, cancellationToken);

    public async Task StampChangedAsync(Guid userId)
    {
        // The post-commit work must finish even when the request was aborted, so it ignores the request token.
        var key = Key(userId);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                // Re-read the committed stamp, so the cache never receives a value that was not persisted.
                var stamp = await source.ReadAsync(userId, CancellationToken.None);
                if (stamp is null)
                {
                    await cache.RemoveAsync(key, CancellationToken.None);
                }
                else
                {
                    await cache.SetAsync(key, stamp, EntryOptions, tags: null, CancellationToken.None);
                }

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                OverwriteFailed(logger, userId, attempt, exception.GetType().Name);
            }
        }

        try
        {
            await cache.RemoveAsync(key, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            InvalidationFailed(logger, userId, (int)MaxLifetime.TotalSeconds, exception.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Security stamp cache overwrite failed for user {UserId} (attempt {Attempt}): {ErrorType}.")]
    private static partial void OverwriteFailed(ILogger logger, Guid userId, int attempt, string errorType);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Security stamp cache invalidation failed for user {UserId}; the entry expires within {Seconds} s: {ErrorType}.")]
    private static partial void InvalidationFailed(ILogger logger, Guid userId, int seconds, string errorType);
}