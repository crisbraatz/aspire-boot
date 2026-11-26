using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AspireBoot.Tests.Unit.Api.Identity.Shared;

[Trait("Category", "Unit")]
public sealed class SecurityStampPrincipalValidatorTests
{
    private static readonly Guid UserId = Guid.Parse("0198b000-0000-7000-8000-000000000002");
    private static readonly Guid SessionId = Guid.Parse("0198b000-0000-7000-8000-0000000000b2");
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly IdentityOptions Options = new();

    [Fact]
    public async Task MatchingStamp_IsValid()
    {
        var sut = Create(new FakeCache { Cached = "stamp-1" });

        Assert.True(await sut.IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task MismatchedStamp_IsRejected()
    {
        var sut = Create(new FakeCache { Cached = "stamp-2" });

        Assert.False(await sut.IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task MissingStampClaim_IsRejected_WithoutTouchingTheCache()
    {
        var cache = new FakeCache { Cached = "stamp-1" };
        var sut = Create(cache);

        Assert.False(await sut.IsValidAsync(Principal(null), CancellationToken.None));
        Assert.Equal(0, cache.CacheReads);
    }

    [Fact]
    public async Task MissingUserIdClaim_IsRejected()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Options.ClaimsIdentity.SecurityStampClaimType, "stamp-1")], "test"));

        Assert.False(await Create(new FakeCache { Cached = "stamp-1" }).IsValidAsync(principal, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownAccount_EmptyStamp_IsRejected()
    {
        var sut = Create(new FakeCache { Cached = string.Empty });

        Assert.False(await sut.IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task CacheThrows_FallsBackToDatabase_AndAcceptsAMatch()
    {
        var cache = new FakeCache { CacheThrows = true, Database = "stamp-1" };

        Assert.True(await Create(cache).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
        Assert.Equal(1, cache.DatabaseReads);
    }

    [Fact]
    public async Task CacheThrows_DatabaseHasNewStamp_IsRejected()
    {
        var cache = new FakeCache { CacheThrows = true, Database = "stamp-2" };

        Assert.False(await Create(cache).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task CacheAndDatabaseThrow_IsRejected_NeverFailsOpen()
    {
        var cache = new FakeCache { CacheThrows = true, DatabaseThrows = true };

        Assert.False(await Create(cache).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var cache = new FakeCache { CacheCancels = true };

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Create(cache).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task MissingSessionClaim_IsRejected_WithoutTouchingTheCaches()
    {
        var sessions = new FakeSessions();

        Assert.False(await Create(new FakeCache { Cached = "stamp-1" }, sessions).IsValidAsync(Principal("stamp-1", sid: null), CancellationToken.None));
        Assert.Equal(0, sessions.CacheReads);
    }

    [Fact]
    public async Task SessionOfAnotherUser_IsRejected()
    {
        var sessions = new FakeSessions { State = Active() with { UserId = Guid.NewGuid() } };

        Assert.False(await Create(new FakeCache { Cached = "stamp-1" }, sessions).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task RevokedSession_IsRejected()
    {
        var sessions = new FakeSessions { State = Active() with { Active = false } };

        Assert.False(await Create(new FakeCache { Cached = "stamp-1" }, sessions).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredSession_IsRejected()
    {
        var sessions = new FakeSessions { State = Active() with { ExpiresAt = Now } };

        Assert.False(await Create(new FakeCache { Cached = "stamp-1" }, sessions).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task SessionCacheThrows_FallsBackToDatabase()
    {
        var sessions = new FakeSessions { CacheThrows = true, State = Active() };

        Assert.True(await Create(new FakeCache { Cached = "stamp-1" }, sessions).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
        Assert.Equal(1, sessions.DatabaseReads);
    }

    [Fact]
    public async Task SessionCacheAndDatabaseThrow_IsRejected()
    {
        var sessions = new FakeSessions { CacheThrows = true, DatabaseThrows = true };

        Assert.False(await Create(new FakeCache { Cached = "stamp-1" }, sessions).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task StampMismatch_IsRejected_EvenWithAnActiveSession()
    {
        Assert.False(await Create(new FakeCache { Cached = "stamp-2" }, new FakeSessions { State = Active() }).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    [Fact]
    public async Task SeenWithinTheThrottle_DoesNotTouch()
    {
        var heartbeat = new FakeHeartbeat();

        Assert.True(await Create(new FakeCache { Cached = "stamp-1" }, new FakeSessions { State = Active() with { LastSeenAt = Now.AddMinutes(-4) } }, heartbeat)
            .IsValidAsync(Principal("stamp-1"), CancellationToken.None));
        Assert.Equal(0, heartbeat.Calls);
    }

    [Fact]
    public async Task SeenBeyondTheThrottle_TouchesOnce_AndInvalidatesInsteadOfSetting()
    {
        var heartbeat = new FakeHeartbeat { Result = true };
        var sessions = new FakeSessions { State = Active() with { LastSeenAt = Now.AddMinutes(-5) } };

        Assert.True(await Create(new FakeCache { Cached = "stamp-1" }, sessions, heartbeat).IsValidAsync(Principal("stamp-1"), CancellationToken.None));

        Assert.Equal(1, heartbeat.Calls);
        Assert.Equal([SessionId], sessions.Invalidated);
    }

    [Fact]
    public async Task LostTheHeartbeatRace_DoesNotInvalidate()
    {
        var heartbeat = new FakeHeartbeat { Result = false };
        var sessions = new FakeSessions { State = Active() with { LastSeenAt = Now.AddHours(-1) } };

        Assert.True(await Create(new FakeCache { Cached = "stamp-1" }, sessions, heartbeat).IsValidAsync(Principal("stamp-1"), CancellationToken.None));

        Assert.Empty(sessions.Invalidated);
    }

    [Fact]
    public async Task HeartbeatFailure_NeverRejectsTheSession()
    {
        var heartbeat = new FakeHeartbeat { Throws = true };
        var sessions = new FakeSessions { State = Active() with { LastSeenAt = Now.AddHours(-1) } };

        Assert.True(await Create(new FakeCache { Cached = "stamp-1" }, sessions, heartbeat).IsValidAsync(Principal("stamp-1"), CancellationToken.None));
    }

    /// <summary>
    /// The heartbeat commits, then a revocation commits and writes "inactive" to the cache, then the heartbeat's cache step runs.
    /// Because that step only removes the entry, the next read reloads the committed (revoked) row and is not Active.
    /// </summary>
    [Fact]
    public async Task RevocationBetweenTheHeartbeatAndTheCacheStep_NeverComesBackAsActive()
    {
        var store = new CommittedSessionSource(Active() with { LastSeenAt = Now.AddHours(-1) });
        var hybrid = new DictionaryHybridCache();
        var realCache = new UserSessionCache(hybrid, store, NullLogger<UserSessionCache>.Instance);
        var heartbeat = new FakeHeartbeat
        {
            Result = true,
            AfterCommit = async () =>
            {
                store.Revoke();
                await realCache.SessionsChangedAsync([SessionId]);
            },
        };
        var sut = new SecurityStampPrincipalValidator(
            new FakeCache { Cached = "stamp-1" },
            realCache,
            heartbeat,
            Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(new AspireBoot.Api.Identity.Shared.UserSessionOptions()),
            new FakeTimeProvider(Now),
            NullLogger<SecurityStampPrincipalValidator>.Instance);

        Assert.True(await sut.IsValidAsync(Principal("stamp-1"), CancellationToken.None));

        var next = await realCache.GetAsync(SessionId, CancellationToken.None);
        Assert.False(next.Active);
    }

    private static SessionState Active() => new(UserId, true, Now.AddHours(8), Now);

    private static SecurityStampPrincipalValidator Create(FakeCache cache, FakeSessions? sessions = null, FakeHeartbeat? heartbeat = null) =>
        new(
            cache,
            sessions ?? new FakeSessions { State = Active() },
            heartbeat ?? new FakeHeartbeat(),
            Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(new AspireBoot.Api.Identity.Shared.UserSessionOptions()),
            new FakeTimeProvider(Now),
            NullLogger<SecurityStampPrincipalValidator>.Instance);

    private static ClaimsPrincipal Principal(string? stamp, string? sid = "default")
    {
        var claims = new List<Claim> { new(Options.ClaimsIdentity.UserIdClaimType, UserId.ToString()) };
        if (stamp is not null)
        {
            claims.Add(new Claim(Options.ClaimsIdentity.SecurityStampClaimType, stamp));
        }

        if (sid is not null)
        {
            claims.Add(new Claim(SessionClaims.SessionId, SessionId.ToString("D")));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed class FakeSessions : IUserSessionCache
    {
        public SessionState? State { get; init; }

        public bool CacheThrows { get; init; }

        public bool DatabaseThrows { get; init; }

        public int CacheReads { get; private set; }

        public int DatabaseReads { get; private set; }

        public List<Guid> Invalidated { get; } = [];

        public ValueTask<SessionState> GetAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            CacheReads++;
            return CacheThrows ? throw new InvalidOperationException("redis down") : ValueTask.FromResult(State!);
        }

        public Task<SessionState> GetFromDatabaseAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            DatabaseReads++;
            return DatabaseThrows ? throw new InvalidOperationException("db down") : Task.FromResult(State!);
        }

        public Task SessionsChangedAsync(IReadOnlyCollection<Guid> sessionIds) => Task.CompletedTask;

        public Task InvalidateAsync(Guid sessionId)
        {
            Invalidated.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHeartbeat : IUserSessionHeartbeat
    {
        public bool Result { get; init; }

        public bool Throws { get; init; }

        public Func<Task>? AfterCommit { get; init; }

        public int Calls { get; private set; }

        public async Task<bool> TouchAsync(Guid sessionId, Guid userId, DateTimeOffset now, TimeSpan throttle, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throws)
            {
                throw new InvalidOperationException("db down");
            }

            if (AfterCommit is not null)
            {
                await AfterCommit();
            }

            return Result;
        }
    }

    private sealed class CommittedSessionSource(SessionState initial) : IUserSessionSource
    {
        private SessionState _state = initial;

        public void Revoke() => _state = _state with { Active = false };

        public Task<SessionState?> ReadAsync(Guid sessionId, CancellationToken cancellationToken) => Task.FromResult<SessionState?>(_state);
    }

    private sealed class DictionaryHybridCache : HybridCache
    {
        private readonly Dictionary<string, object> _entries = [];

        public override async ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                return (T)existing;
            }

            var created = await factory(state, cancellationToken);
            _entries[key] = created!;
            return created;
        }

        public override ValueTask SetAsync<T>(
            string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            _entries[key] = value!;
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _entries.Remove(key);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeCache : ISecurityStampCache
    {
        public string Cached { get; init; } = string.Empty;

        public string? Database { get; init; }

        public bool CacheThrows { get; init; }

        public bool CacheCancels { get; init; }

        public bool DatabaseThrows { get; init; }

        public int CacheReads { get; private set; }

        public int DatabaseReads { get; private set; }

        public ValueTask<string> GetStampAsync(Guid userId, CancellationToken cancellationToken)
        {
            CacheReads++;
            if (CacheCancels)
            {
                throw new OperationCanceledException();
            }

            return CacheThrows ? throw new InvalidOperationException("redis down") : ValueTask.FromResult(Cached);
        }

        public Task<string?> GetStampFromDatabaseAsync(Guid userId, CancellationToken cancellationToken)
        {
            DatabaseReads++;
            return DatabaseThrows ? throw new InvalidOperationException("db down") : Task.FromResult(Database);
        }

        public Task StampChangedAsync(Guid userId) => Task.CompletedTask;
    }
}