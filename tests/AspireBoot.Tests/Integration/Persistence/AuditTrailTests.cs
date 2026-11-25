using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Npgsql;

using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class AuditTrailTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri PasskeysUri = new("/api/identity/account/passkeys", UriKind.Relative);
    private static readonly Uri AddPasskeyOptionsUri = new("/api/identity/account/passkeys/options", UriKind.Relative);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    public ValueTask InitializeAsync()
    {
        // The Worker sends the confirmation email that the registration flow waits for.
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _worker.DisposeAsync();
    }

    [Fact]
    public async Task SignUp_WritesAnonymousInsertRows_WithMaskedStamps()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();

        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "audit-signup@aspireboot.test", ct);

        var insert = await QuerySingleAsync(
            "select actor_user_id, actor_type, changes::text from audit_entries where entity_type = 'User' and entity_key = @key and action = 'insert'",
            userId.ToString(), ct);
        Assert.Null(insert[0]);
        Assert.Equal("system", insert[1]);

        using var changes = JsonDocument.Parse((string)insert[2]!);
        Assert.Equal("***", changes.RootElement.GetProperty("SecurityStamp").GetProperty("new").GetString());
        Assert.Equal("***", changes.RootElement.GetProperty("ConcurrencyStamp").GetProperty("new").GetString());
        Assert.Equal("audit-signup@aspireboot.test", changes.RootElement.GetProperty("Email").GetProperty("new").GetString());
    }

    [Fact]
    public async Task CompleteRegistration_AttributesItsRowsToTheOwnerOfTheLink()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();

        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "audit-owner@aspireboot.test", ct);

        var confirmation = await QuerySingleAsync(
            "select actor_user_id, actor_type from audit_entries where entity_type = 'User' and entity_key = @key and action = 'update' and changes ? 'EmailConfirmed'",
            userId.ToString(), ct);
        Assert.Equal(userId, confirmation[0]);
        Assert.Equal("user", confirmation[1]);

        var passkeys = await QueryAsync(
            "select actor_user_id from audit_entries where entity_type like '%Passkey%' and action = 'insert'", ct);
        Assert.NotEmpty(passkeys);
        Assert.All(passkeys, row => Assert.Equal(userId, row[0]));
    }

    [Fact]
    public async Task Passkey_IsAuditedWithoutPublicKeyOrAttestation()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var first = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, first, "audit-passkey@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var second = new SoftwareAuthenticator();
        using (var options = await client.SendUnsafeAsync(HttpMethod.Post, AddPasskeyOptionsUri, null, ct))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode);
            var creationOptions = await options.Content.ReadAsStringAsync(ct);
            using var added = await client.SendUnsafeAsync(HttpMethod.Post, PasskeysUri, JsonContent.Create(new
            {
                credential = JsonDocument.Parse(second.CreateAttestation(creationOptions)).RootElement,
                name = "Notebook",
            }), ct);
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        var rows = await QueryAsync(
            "select actor_user_id, changes::text from audit_entries where entity_type like '%Passkey%' order by occurred_at",
            ct);
        Assert.True(rows.Count >= 2, "Both passkeys must be audited.");
        Assert.Contains(rows, row => row[0] is Guid actor && actor == userId);

        foreach (var row in rows)
        {
            var raw = (string)row[1]!;
            foreach (var forbidden in new[] { "publickey", "attestation", "clientdatajson", "authenticatordata", "transports" })
            {
                Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains("\"changed\": true", raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RolledBackTransaction_LeavesNoAuditRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();

        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            context.Users.Add(NewUser("rollback@aspireboot.test"));
            await context.SaveChangesAsync(ct);
            await transaction.RollbackAsync(ct);
        }

        Assert.Equal(0L, await ScalarAsync("select count(*) from audit_entries", ct));
        Assert.Equal(0L, await ScalarAsync("select count(*) from \"AspNetUsers\"", ct));
    }

    [Fact]
    public async Task WolverineSaveChangesAndFlush_WritesAuditRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        var user = NewUser("wolverine@aspireboot.test");

        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<AppDbContext>>();
            outbox.DbContext.Users.Add(user);
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }

        Assert.Equal(
            1L,
            await ScalarAsync($"select count(*) from audit_entries where entity_type = 'User' and entity_key = '{user.Id}' and action = 'insert'", ct));
    }

    [Fact]
    public async Task AuditEntries_RejectUpdateDeleteAndTruncate()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Users.Add(NewUser("append-only@aspireboot.test"));
            await context.SaveChangesAsync(ct);
        }

        foreach (var sql in new[] { "update audit_entries set action = 'x'", "delete from audit_entries", "truncate audit_entries" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, ct));
            Assert.Contains("append-only", error.MessageText, StringComparison.Ordinal);
        }

        Assert.True(await ScalarAsync("select count(*) from audit_entries", ct) > 0);
    }

    [Fact]
    public async Task ResetDatabase_ClearsAuditEntries()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Users.Add(NewUser("reset@aspireboot.test"));
            await context.SaveChangesAsync(ct);
        }

        Assert.True(await ScalarAsync("select count(*) from audit_entries", ct) > 0);

        await infrastructure.ResetDatabaseAsync();

        Assert.Equal(0L, await ScalarAsync("select count(*) from audit_entries", ct));
    }

    private static User NewUser(string email) =>
        User.Create(email, DisplayName.Create("Ada Lovelace").Value, UiCulture.PtBr, new FakeTimeProvider(Now));

    private async Task<long> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<object?[]> QuerySingleAsync(string sql, string key, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct), "Expected one audit row.");
        var values = new object?[reader.FieldCount];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
        }

        return values;
    }

    private async Task<List<object?[]>> QueryAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
            }

            rows.Add(values);
        }

        return rows;
    }
}