using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

using Npgsql;

namespace AspireBoot.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
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

    public async ValueTask InitializeAsync() => await infrastructure.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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