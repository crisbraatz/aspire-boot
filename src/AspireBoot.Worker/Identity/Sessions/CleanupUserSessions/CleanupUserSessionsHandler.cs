using System.ComponentModel.DataAnnotations;

using AspireBoot.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using CleanupMessage = AspireBoot.Core.Messaging.Identity.CleanupUserSessions;

namespace AspireBoot.Worker.Identity.Sessions.CleanupUserSessions;

/// <summary>Cleanup settings, bound from the <c>UserSessions</c> section (the same section the API reads).</summary>
public sealed class UserSessionsCleanupOptions
{
    public const string SectionName = "UserSessions";

    /// <summary>How long revoked or expired rows are kept before they are deleted.</summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "3650.00:00:00")]
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(30);
}

/// <summary>
/// Deletes session rows whose revocation or expiry is older than the retention, in batches. The deletes are housekeeping
/// and deliberately unaudited (ADR 0007): the creation and the revocation of every row were audited when they happened.
/// </summary>
public sealed class CleanupUserSessionsHandler
{
    private const int BatchSize = 1000;

    public static async Task Handle(
        CleanupMessage message,
        AppDbContext db,
        TimeProvider clock,
        IOptions<UserSessionsCleanupOptions> options,
        ILogger<CleanupUserSessionsHandler> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var cutoff = clock.GetUtcNow() - options.Value.Retention;
        var total = 0;

        while (true)
        {
            var batch = await db.UserSessions
                .Where(session => session.RevokedAt < cutoff || session.ExpiresAt < cutoff)
                .OrderBy(session => session.Id)
                .Select(session => session.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            total += await db.UserSessions
                .Where(session => batch.Contains(session.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        UserSessionsLog.SessionsCleaned(logger, total);
        if (total > 0)
        {
            UserSessionsLog.RecordCleaned(total);
        }
    }
}