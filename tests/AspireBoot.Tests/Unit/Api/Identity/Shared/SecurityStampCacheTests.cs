using AspireBoot.Api.Identity.Shared;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;

namespace AspireBoot.Tests.Unit.Api.Identity.Shared;

[Trait("Category", "Unit")]
public sealed class SecurityStampCacheTests
{
    private static readonly Guid UserId = Guid.Parse("0198b000-0000-7000-8000-000000000001");

    [Fact]
    public async Task StampChanged_ReReadsCommittedStamp_AndSetsItWithReadOptions()
    {
        var cache = new FakeHybridCache();
        var sut = Create(cache, new FakeSource("committed"));

        await sut.StampChangedAsync(UserId);

        var set = Assert.Single(cache.Sets);
        Assert.Equal("identity:stamp:" + UserId.ToString("D"), set.Key);
        Assert.Equal("committed", set.Value);
        Assert.Null(set.Tags);
        Assert.Equal(TimeSpan.FromSeconds(60), set.Options!.Expiration);
        Assert.Equal(HybridCacheEntryFlags.DisableLocalCache, set.Options.Flags);
    }

    [Fact]
    public async Task GetStamp_UsesTheSameOptionsAsTheWrite_AndNoTags()
    {
        var cache = new FakeHybridCache();
        var sut = Create(cache, new FakeSource("db-stamp"));

        var stamp = await sut.GetStampAsync(UserId, CancellationToken.None);

        Assert.Equal("db-stamp", stamp);
        var read = Assert.Single(cache.Reads);
        Assert.Null(read.Tags);
        Assert.Equal(TimeSpan.FromSeconds(60), read.Options!.Expiration);
        Assert.Equal(HybridCacheEntryFlags.DisableLocalCache, read.Options.Flags);
    }

    [Fact]
    public async Task SetThrowsOnce_ThenSucceeds_RetriesAndDoesNotRemove()
    {
        var cache = new FakeHybridCache { SetFailures = 1 };
        var sut = Create(cache, new FakeSource("committed"));

        await sut.StampChangedAsync(UserId);

        Assert.Equal(2, cache.SetAttempts);
        Assert.Single(cache.Sets);
        Assert.Empty(cache.Removed);
    }

    [Fact]
    public async Task SetThrowsTwice_RemovesTheEntry()
    {
        var cache = new FakeHybridCache { SetFailures = 2 };
        var sut = Create(cache, new FakeSource("committed"));

        await sut.StampChangedAsync(UserId);

        Assert.Equal(2, cache.SetAttempts);
        Assert.Empty(cache.Sets);
        Assert.Equal(["identity:stamp:" + UserId.ToString("D")], cache.Removed);
    }

    [Fact]
    public async Task EverythingFails_IsLoggedAndDoesNotThrow()
    {
        var cache = new FakeHybridCache { SetFailures = 2, RemoveFails = true };
        var logger = new CapturingLogger<SecurityStampCache>();
        var sut = new SecurityStampCache(cache, new FakeSource("committed"), logger);

        await sut.StampChangedAsync(UserId);

        Assert.Contains(logger.Messages, message => message.Contains(UserId.ToString(), StringComparison.Ordinal) && message.Contains("60", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("committed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownAccount_RemovesTheEntry()
    {
        var cache = new FakeHybridCache();
        var sut = Create(cache, new FakeSource(null));

        await sut.StampChangedAsync(UserId);

        Assert.Empty(cache.Sets);
        Assert.Single(cache.Removed);
    }

    private static SecurityStampCache Create(FakeHybridCache cache, FakeSource source) =>
        new(cache, source, NullLogger<SecurityStampCache>.Instance);

    private sealed class FakeSource(string? stamp) : ISecurityStampSource
    {
        public Task<string?> ReadAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(stamp);
    }

    private sealed record Entry(string Key, object? Value, HybridCacheEntryOptions? Options, IEnumerable<string>? Tags);

    private sealed class FakeHybridCache : HybridCache
    {
        public int SetFailures { get; init; }

        public bool RemoveFails { get; init; }

        public int SetAttempts { get; private set; }

        public List<Entry> Sets { get; } = [];

        public List<Entry> Reads { get; } = [];

        public List<string> Removed { get; } = [];

        public override async ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            Reads.Add(new Entry(key, null, options, tags));
            return await factory(state, cancellationToken);
        }

        public override ValueTask SetAsync<T>(
            string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            SetAttempts++;
            if (SetAttempts <= SetFailures)
            {
                throw new InvalidOperationException("redis down");
            }

            Sets.Add(new Entry(key, value, options, tags));
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            if (RemoveFails)
            {
                throw new InvalidOperationException("redis down");
            }

            Removed.Add(key);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The stamp cache never uses tags.");
    }

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}