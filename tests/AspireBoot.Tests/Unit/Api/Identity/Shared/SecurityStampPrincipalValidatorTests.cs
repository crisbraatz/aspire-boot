using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AspireBoot.Tests.Unit.Api.Identity.Shared;

[Trait("Category", "Unit")]
public sealed class SecurityStampPrincipalValidatorTests
{
    private static readonly Guid UserId = Guid.Parse("0198b000-0000-7000-8000-000000000002");
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

    private static SecurityStampPrincipalValidator Create(FakeCache cache) =>
        new(cache, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<SecurityStampPrincipalValidator>.Instance);

    private static ClaimsPrincipal Principal(string? stamp)
    {
        var claims = new List<Claim> { new(Options.ClaimsIdentity.UserIdClaimType, UserId.ToString()) };
        if (stamp is not null)
        {
            claims.Add(new Claim(Options.ClaimsIdentity.SecurityStampClaimType, stamp));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
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