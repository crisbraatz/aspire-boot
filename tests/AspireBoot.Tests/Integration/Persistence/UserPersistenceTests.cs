using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

using Npgsql;

namespace AspireBoot.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class UserPersistenceTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migrations_CreatePasskeyTable()
    {
        var exists = await ScalarAsync(
            "select count(*) from information_schema.tables where table_schema = 'public' and table_name = 'AspNetUserPasskeys'",
            TestContext.Current.CancellationToken);

        Assert.Equal(1L, exists);
    }

    [Fact]
    public async Task Migrations_CreatePreferredCultureColumnWithDefault()
    {
        var column = await ScalarAsync(
            "select count(*) from information_schema.columns where table_name = 'AspNetUsers' and column_name = 'preferred_culture' and is_nullable = 'NO' and character_maximum_length = 10 and column_default like '%en-US%'",
            TestContext.Current.CancellationToken);

        Assert.Equal(1L, column);
    }

    [Fact]
    public async Task Model_HasNoPendingChangesAgainstMigrations()
    {
        await using var context = CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task User_RoundTripsPreferredCulture()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = User.Create("ada@example.com", DisplayName.Create("Ada Lovelace").Value, UiCulture.PtBr, new FakeTimeProvider(Now));

        await using (var writer = CreateContext())
        {
            writer.Users.Add(user);
            await writer.SaveChangesAsync(ct);
        }

        await using var reader = CreateContext();
        var culture = await reader.Users
            .AsNoTracking()
            .Where(u => u.Id == user.Id)
            .Select(u => u.PreferredCulture)
            .SingleAsync(ct);

        Assert.Equal(UiCulture.PtBr, culture);
    }

    [Fact]
    public async Task User_RoundTripsAvatarThroughSetReplaceAndRemove()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = User.Create("grace@example.com", DisplayName.Create("Grace Hopper").Value, UiCulture.EnUs, new FakeTimeProvider(Now));
        await using (var writer = CreateContext())
        {
            writer.Users.Add(user);
            await writer.SaveChangesAsync(ct);
        }

        var first = Guid.NewGuid();
        await UpdateUserAsync(user.Id, u => u.ChangeAvatar(first), ct);
        var afterSet = await ReadUserAsync(user.Id, ct);
        Assert.Equal(AvatarReference.For(user.Id, first, 1), afterSet.Avatar);
        Assert.Equal(1, afterSet.AvatarVersion);

        var second = Guid.NewGuid();
        await UpdateUserAsync(user.Id, u => u.ChangeAvatar(second), ct);
        var afterReplace = await ReadUserAsync(user.Id, ct);
        Assert.Equal(AvatarReference.For(user.Id, second, 2), afterReplace.Avatar);
        Assert.Equal(2, afterReplace.AvatarVersion);

        await UpdateUserAsync(user.Id, u => u.RemoveAvatar(), ct);
        var afterRemove = await ReadUserAsync(user.Id, ct);
        Assert.Null(afterRemove.Avatar);
        Assert.Equal(2, afterRemove.AvatarVersion);
    }

    [Fact]
    public async Task ConcurrentAvatarChange_IsDetectedThroughRowVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = User.Create("linus@example.com", DisplayName.Create("Linus Torvalds").Value, UiCulture.EnUs, new FakeTimeProvider(Now));
        await using (var writer = CreateContext())
        {
            writer.Users.Add(user);
            await writer.SaveChangesAsync(ct);
        }

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstUser = await first.Users.SingleAsync(u => u.Id == user.Id, ct);
        var secondUser = await second.Users.SingleAsync(u => u.Id == user.Id, ct);

        firstUser.ChangeAvatar(Guid.NewGuid());
        secondUser.ChangeAvatar(Guid.NewGuid());
        await first.SaveChangesAsync(ct);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Migrations_AddAvatarColumnsWithoutXminColumn()
    {
        var columns = await ScalarAsync(
            "select count(*) from information_schema.columns where table_name = 'AspNetUsers' and column_name in ('avatar_key', 'avatar_version')",
            TestContext.Current.CancellationToken);
        var xmin = await ScalarAsync(
            "select count(*) from information_schema.columns where table_name = 'AspNetUsers' and column_name = 'xmin'",
            TestContext.Current.CancellationToken);

        Assert.Equal(2L, columns);
        Assert.Equal(0L, xmin);
    }

    public async ValueTask InitializeAsync() => await infrastructure.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<User> ReadUserAsync(Guid userId, CancellationToken ct)
    {
        await using var reader = CreateContext();
        return await reader.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
    }

    private async Task UpdateUserAsync(Guid userId, Action<User> change, CancellationToken ct)
    {
        await using var writer = CreateContext();
        var user = await writer.Users.SingleAsync(u => u.Id == userId, ct);
        change(user);
        await writer.SaveChangesAsync(ct);
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