using AspireBoot.Core.Identity.Domain;

namespace AspireBoot.Tests.Unit.Identity;

[Trait("Category", "Unit")]
public sealed class UserSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private static UserSession StartSession(Guid? userId = null, bool isPersistent = false) =>
        UserSession.Start(
            userId ?? Guid.NewGuid(),
            Now,
            Lifetime,
            isPersistent,
            UserAgentSummary.FromStored("chrome|windows"),
            MaskedIpAddress.From(System.Net.IPAddress.Parse("192.168.1.25")));

    [Fact]
    public void Start_SetsTheSessionFields()
    {
        var userId = Guid.NewGuid();

        var session = StartSession(userId, isPersistent: true);

        Assert.Equal(userId, session.UserId);
        Assert.Equal(Now, session.CreatedAt);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(Now + Lifetime, session.ExpiresAt);
        Assert.True(session.IsPersistent);
        Assert.Null(session.RevokedAt);
        Assert.Equal("chrome|windows", session.UserAgent);
        Assert.Equal("192.168.*.*", session.IpMasked);
    }

    [Fact]
    public void Start_GeneratesVersion7Ids()
    {
        var session = StartSession();

        Assert.Equal('7', session.Id.ToString("D")[14]);
    }

    [Fact]
    public void Start_GeneratesDistinctIds()
    {
        Assert.NotEqual(StartSession().Id, StartSession().Id);
    }

    [Fact]
    public void Start_WithEmptyUserId_Throws()
    {
        Assert.Throws<ArgumentException>(() => UserSession.Start(
            Guid.Empty, Now, Lifetime, false, UserAgentSummary.Unknown, MaskedIpAddress.From(null)));
    }

    [Fact]
    public void Start_WithNonPositiveLifetime_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UserSession.Start(
            Guid.NewGuid(), Now, TimeSpan.Zero, false, UserAgentSummary.Unknown, MaskedIpAddress.From(null)));
    }

    [Fact]
    public void IsActive_BeforeExpiryAndNotRevoked_IsTrue()
    {
        var session = StartSession();

        Assert.True(session.IsActive(Now + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void IsActive_AtExpiry_IsFalse()
    {
        var session = StartSession();

        Assert.False(session.IsActive(session.ExpiresAt));
    }

    [Fact]
    public void IsActive_AfterExpiry_IsFalse()
    {
        var session = StartSession();

        Assert.False(session.IsActive(session.ExpiresAt.AddSeconds(1)));
    }

    [Fact]
    public void IsActive_AfterRevoke_IsFalse()
    {
        var session = StartSession();
        session.Revoke(Now + TimeSpan.FromMinutes(5));

        Assert.False(session.IsActive(Now + TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public void Revoke_SetsRevokedAt()
    {
        var session = StartSession();
        var revokedAt = Now + TimeSpan.FromMinutes(5);

        session.Revoke(revokedAt);

        Assert.Equal(revokedAt, session.RevokedAt);
    }

    [Fact]
    public void Revoke_TwiceKeepsTheFirstRevokedAt()
    {
        var session = StartSession();
        var first = Now + TimeSpan.FromMinutes(5);

        session.Revoke(first);
        session.Revoke(first + TimeSpan.FromMinutes(10));

        Assert.Equal(first, session.RevokedAt);
    }
}