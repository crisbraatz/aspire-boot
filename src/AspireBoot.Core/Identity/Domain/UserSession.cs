namespace AspireBoot.Core.Identity.Domain;

/// <summary>
/// One signed-in device or browser of an account. Revocation is idempotent and keeps the first revocation time.
/// There is deliberately no row-version token (ADR 0007): the heartbeat excludes revoked rows, so last writer wins safely.
/// </summary>
public sealed class UserSession
{
    private const int MaxSummaryLength = 64;

    private UserSession()
    {
        UserAgent = string.Empty;
        IpMasked = string.Empty;
    }

    private UserSession(
        Guid id,
        Guid userId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        bool isPersistent,
        string userAgent,
        string ipMasked)
    {
        Id = id;
        UserId = userId;
        CreatedAt = now;
        LastSeenAt = now;
        ExpiresAt = expiresAt;
        IsPersistent = isPersistent;
        UserAgent = userAgent;
        IpMasked = ipMasked;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsPersistent { get; private set; }

    public string UserAgent { get; private set; }

    public string IpMasked { get; private set; }

    public static UserSession Start(
        Guid userId,
        DateTimeOffset now,
        TimeSpan lifetime,
        bool isPersistent,
        UserAgentSummary userAgent,
        MaskedIpAddress ipAddress)
    {
        ArgumentNullException.ThrowIfNull(userAgent);
        ArgumentNullException.ThrowIfNull(ipAddress);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user id must not be empty.", nameof(userId));
        }

        var summary = userAgent.Value;
        var ip = ipAddress.Value;
        if (summary.Length > MaxSummaryLength || ip.Length > MaxSummaryLength)
        {
            throw new ArgumentException("The session summary exceeds its maximum length.");
        }

        return new UserSession(Guid.CreateVersion7(), userId, now, now + lifetime, isPersistent, summary, ip);
    }

    /// <summary>Ends the session. Calling it again keeps the first revocation time.</summary>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}