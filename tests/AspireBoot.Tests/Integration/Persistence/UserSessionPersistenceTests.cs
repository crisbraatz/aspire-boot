using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Npgsql;

namespace AspireBoot.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class UserSessionPersistenceTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly AspireBootApiFactory _api = new(infrastructure);

    public async ValueTask InitializeAsync() => await infrastructure.ResetDatabaseAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Session_RoundTripsAndIsAuditedOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AddUserAsync(ct);
        var session = UserSession.Start(
            userId, Now, TimeSpan.FromHours(8), isPersistent: true, UserAgentSummary.Parse("Mozilla/5.0 Chrome/120.0 Safari/537.36"), MaskedIpAddress.From(null));

        // The audit interceptor is registered by the application host, so the session is written through it.
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            writer.UserSessions.Add(session);
            await writer.SaveChangesAsync(ct);
        }

        await using var reader = CreateContext();
        var stored = await reader.UserSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id, ct);
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(session.ExpiresAt, stored.ExpiresAt);
        Assert.True(stored.IsPersistent);
        Assert.Null(stored.RevokedAt);
        Assert.Equal(session.UserAgent, stored.UserAgent);
        Assert.Equal(session.IpMasked, stored.IpMasked);

        var audits = await ScalarAsync(
            $"select count(*) from audit_entries where entity_type = 'UserSession' and entity_key = '{session.Id}'", ct);
        Assert.Equal(1L, audits);
    }

    [Fact]
    public async Task Migration_CreatesTableForeignKeyIndexesAndNoXmin()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(1L, await ScalarAsync("select count(*) from information_schema.tables where table_name = 'user_sessions'", ct));
        Assert.Equal(3L, await ScalarAsync("select count(*) from pg_indexes where tablename = 'user_sessions' and indexname like 'ix_user_sessions_%'", ct));
        Assert.Equal(1L, await ScalarAsync(
            "select count(*) from information_schema.referential_constraints where constraint_name like 'fk_user_sessions_%' and delete_rule = 'CASCADE'", ct));
        Assert.Equal(0L, await ScalarAsync("select count(*) from information_schema.columns where table_name = 'user_sessions' and column_name = 'xmin'", ct));
    }

    [Fact]
    public async Task DeletingTheUser_CascadesToSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AddUserAsync(ct);
        await using (var writer = CreateContext())
        {
            writer.UserSessions.Add(UserSession.Start(
                userId, Now, TimeSpan.FromHours(1), false, UserAgentSummary.Unknown, MaskedIpAddress.From(null)));
            await writer.SaveChangesAsync(ct);
        }

        await using (var deleter = CreateContext())
        {
            await deleter.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
        }

        Assert.Equal(0L, await ScalarAsync("select count(*) from user_sessions", ct));
    }

    private async Task<Guid> AddUserAsync(CancellationToken ct)
    {
        var user = User.Create("session@example.com", DisplayName.Create("Session Owner").Value, UiCulture.PtBr, new FakeTimeProvider(Now));
        await using var writer = CreateContext();
        writer.Users.Add(user);
        await writer.SaveChangesAsync(ct);
        return user.Id;
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseAspireBootNpgsql(infrastructure.PostgresConnectionString).UseStandaloneIdentitySchema();
        return new AppDbContext(options.Options);
    }

    private async Task<long> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}