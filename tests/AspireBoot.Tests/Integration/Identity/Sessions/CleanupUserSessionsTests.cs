using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Wolverine;
using Wolverine.Runtime;

namespace AspireBoot.Tests.Integration.Identity.Sessions;

/// <summary>The Worker deletes sessions that ended or expired longer ago than the retention, on a daily recurring schedule (ADR 0007).</summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class CleanupUserSessionsTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly AspireBootWorkerFactory _factory = new(infrastructure);

    public async ValueTask InitializeAsync()
    {
        await infrastructure.ResetDatabaseAsync();
        _ = _factory.Services;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Cleanup_DeletesOldRevokedAndOldExpiredRows_AndKeepsTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var userId = await AddUserAsync(ct);

        var oldRevoked = Session(userId, now.AddDays(-60), revokedAt: now.AddDays(-40));
        var oldExpired = Session(userId, now.AddDays(-60));
        var active = Session(userId, now);
        var recentlyRevoked = Session(userId, now.AddDays(-5), revokedAt: now.AddDays(-1));
        var recentlyExpired = Session(userId, now.AddDays(-10));
        await SaveAsync([oldRevoked, oldExpired, active, recentlyRevoked, recentlyExpired], ct);

        await InvokeAsync(ct);

        var remaining = await IdsAsync(ct);
        Assert.Equivalent(new[] { active.Id, recentlyRevoked.Id, recentlyExpired.Id }, remaining);
    }

    [Fact]
    public async Task Cleanup_DeletesMoreThanOneBatch_AndIsRepeatable()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var userId = await AddUserAsync(ct);
        var old = Enumerable.Range(0, 1050).Select(_ => Session(userId, now.AddDays(-90))).ToList();
        var active = Session(userId, now);
        await SaveAsync([.. old, active], ct);

        await InvokeAsync(ct);
        await InvokeAsync(ct);

        Assert.Equal([active.Id], await IdsAsync(ct));
    }

    [Fact]
    public void TheRecurringSchedule_IsRegisteredDailyAtThreeSeventeenUtc_AndSkipsWhileRunning()
    {
        // SkipWhileRunning puts the schedule on Wolverine's timer agent, which keeps no tracking row, so
        // IRecurringScheduleControl.QueryAsync is empty for it. The registration itself is read from the Worker's options.
        var schedules = _factory.Services.GetRequiredService<IWolverineRuntime>().Options.Schedules;

        var schedule = schedules.FindByName(nameof(CleanupUserSessions));

        Assert.NotNull(schedule);
        Assert.Equal(typeof(CleanupUserSessions), schedule.MessageType);
        Assert.Equal("17 3 * * *", schedule.Schedule.Expression);
        Assert.Equal(TimeZoneInfo.Utc, schedule.Schedule.TimeZone);
        Assert.Equal(RecurringOverlapPolicy.SkipWhileRunning, schedule.OverlapPolicy);
    }

    private static UserSession Session(Guid userId, DateTimeOffset startedAt, DateTimeOffset? revokedAt = null)
    {
        var session = UserSession.Start(userId, startedAt, Lifetime, isPersistent: false, UserAgentSummary.Unknown, MaskedIpAddress.From(null));
        if (revokedAt is { } at)
        {
            session.Revoke(at);
        }

        return session;
    }

    private async Task<Guid> AddUserAsync(CancellationToken ct)
    {
        var user = User.Create("cleanup@example.com", DisplayName.Create("Cleanup Owner").Value, UiCulture.EnUs, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    private async Task SaveAsync(IEnumerable<UserSession> sessions, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserSessions.AddRange(sessions);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Guid>> IdsAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions.AsNoTracking().Select(session => session.Id).ToListAsync(ct);
    }

    private async Task InvokeAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(new CleanupUserSessions(), ct);
    }
}