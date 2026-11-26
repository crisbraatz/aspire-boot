using AspireBoot.Api.Identity.Shared;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;

namespace AspireBoot.Tests.Unit.Api;

[Trait("Category", "Unit")]
public sealed class UserSessionCacheTests
{
    private static readonly Guid SessionId = Guid.Parse("0198b000-0000-7000-8000-0000000000a1");
    private static readonly Guid UserId = Guid.Parse("0198b000-0000-7000-8000-0000000000a2");
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SessionsChanged_ReReadsCommittedRow_AndSetsItWithReadOptions()
    {
        var cache = new FakeHybridCache();
        var state = new SessionState(UserId, true, Now.AddHours(8), Now);
        var sut = Create(cache, new FakeSource(state));

        await sut.SessionsChangedAsync([SessionId]);

        var set = Assert.Single(cache.Sets);
        Assert.Equal("identity:session:" + SessionId.ToString("D"), set.Key);
        Assert.Equal(state, set.Value);
        Assert.Null(set.Tags);
        Assert.Equal(TimeSpan.FromSeconds(60), set.Options!.Expiration);
        Assert.Equal(HybridCacheEntryFlags.DisableLocalCache, set.Options.Flags);
    }

    [Fact]
    public async Task Get_UsesTheSameOptionsAsTheWrite_AndNoTags()
    {
        var cache = new FakeHybridCache();
        var state = new SessionState(UserId, true, Now.AddHours(8), Now);
        var sut = Create(cache, new FakeSource(state));

        var result = await sut.GetAsync(SessionId, CancellationToken.None);

        Assert.Equal(state, result);
        var read = Assert.Single(cache.Reads);
        Assert.Null(read.Tags);
        Assert.Equal(TimeSpan.FromSeconds(60), read.Options!.Expiration);
        Assert.Equal(HybridCacheEntryFlags.DisableLocalCache, read.Options.Flags);
    }

    [Fact]
    public async Task Get_MissingRow_IsCachedAsInactive()
    {
        var sut = Create(new FakeHybridCache(), new FakeSource(null));

        var result = await sut.GetAsync(SessionId, CancellationToken.None);

        Assert.False(result.Active);
    }

    [Fact]
    public async Task GetFromDatabase_BypassesTheCache()
    {
        var cache = new FakeHybridCache();
        var state = new SessionState(UserId, true, Now.AddHours(8), Now);
        var sut = Create(cache, new FakeSource(state));

        var result = await sut.GetFromDatabaseAsync(SessionId, CancellationToken.None);

        Assert.Equal(state, result);
        Assert.Empty(cache.Reads);
    }

    [Fact]
    public async Task SetThrowsOnce_ThenSucceeds_RetriesAndDoesNotRemove()
    {
        var cache = new FakeHybridCache { SetFailures = 1 };
        var sut = Create(cache, new FakeSource(new SessionState(UserId, true, Now, Now)));

        await sut.SessionsChangedAsync([SessionId]);

        Assert.Equal(2, cache.SetAttempts);
        Assert.Single(cache.Sets);
        Assert.Empty(cache.Removed);
    }

    [Fact]
    public async Task SetThrowsTwice_RemovesTheEntry()
    {
        var cache = new FakeHybridCache { SetFailures = 2 };
        var sut = Create(cache, new FakeSource(new SessionState(UserId, true, Now, Now)));

        await sut.SessionsChangedAsync([SessionId]);

        Assert.Empty(cache.Sets);
        Assert.Equal(["identity:session:" + SessionId.ToString("D")], cache.Removed);
    }

    [Fact]
    public async Task EverythingFails_IsLoggedWithIdsOnly_AndDoesNotThrow()
    {
        var cache = new FakeHybridCache { SetFailures = 2, RemoveFails = true };
        var logger = new CapturingLogger<UserSessionCache>();
        var sut = new UserSessionCache(cache, new FakeSource(new SessionState(UserId, true, Now, Now)), logger);

        await sut.SessionsChangedAsync([SessionId]);

        Assert.Contains(logger.Messages, message => message.Contains(SessionId.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("redis down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SessionsChanged_IgnoresACancelledRequest()
    {
        var cache = new FakeHybridCache();
        var sut = Create(cache, new FakeSource(new SessionState(UserId, false, Now, Now)));

        await sut.SessionsChangedAsync([SessionId]);

        Assert.Single(cache.Sets);
    }

    [Fact]
    public async Task SourceFailsTwice_RemovesTheEntry()
    {
        var cache = new FakeHybridCache();
        var sut = Create(cache, new FakeSource(null) { Throws = true });

        await sut.SessionsChangedAsync([SessionId]);

        Assert.Single(cache.Removed);
    }

    [Fact]
    public async Task Invalidate_RemovesTheKey_AndNeverThrows()
    {
        var cache = new FakeHybridCache();
        await Create(cache, new FakeSource(null)).InvalidateAsync(SessionId);
        Assert.Equal(["identity:session:" + SessionId.ToString("D")], cache.Removed);

        var failing = new FakeHybridCache { RemoveFails = true };
        await Create(failing, new FakeSource(null)).InvalidateAsync(SessionId);
    }

    private static UserSessionCache Create(FakeHybridCache cache, FakeSource source) =>
        new(cache, source, NullLogger<UserSessionCache>.Instance);

    private sealed class FakeSource(SessionState? state) : IUserSessionSource
    {
        public bool Throws { get; init; }

        public Task<SessionState?> ReadAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Throws ? throw new InvalidOperationException("db down") : Task.FromResult(state);
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
            throw new NotSupportedException("The session cache never uses tags.");
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