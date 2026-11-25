using System.Net;
using System.Net.Http.Json;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Npgsql;

namespace AspireBoot.Tests.Integration.Persistence;

/// <summary>Aspire pools the DbContext, so the audit actor must come from the current request, never from a captured scope.</summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class AuditActorConcurrencyTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri PreferencesUri = new("/api/identity/account/preferences", UriKind.Relative);

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
    public async Task ConcurrentRequests_RecordTheirOwnActor()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var clientA = new IdentityClient(_api);
        using var clientB = new IdentityClient(_api);
        using var passkeyA = new SoftwareAuthenticator();
        using var passkeyB = new SoftwareAuthenticator();
        var userA = await IdentityFlow.RegisterAsync(infrastructure, clientA, passkeyA, "actor-a@aspireboot.test", ct);
        var userB = await IdentityFlow.RegisterAsync(infrastructure, clientB, passkeyB, "actor-b@aspireboot.test", ct);
        await clientA.RefreshAntiforgeryTokenAsync(ct);
        await clientB.RefreshAntiforgeryTokenAsync(ct);

        const string cultures = "en-US pt-BR es-ES";
        var order = cultures.Split(' ');
        for (var round = 0; round < 6; round++)
        {
            var culture = order[round % order.Length];
            var other = order[(round + 1) % order.Length];
            var responses = await Task.WhenAll(
                SetCultureAsync(clientA, culture, ct),
                SetCultureAsync(clientB, other, ct));
            Assert.All(responses, status => Assert.Equal(HttpStatusCode.NoContent, status));
        }

        var rows = await ActorsAsync(ct);
        Assert.NotEmpty(rows);
        foreach (var (key, actor) in rows)
        {
            Assert.Equal(Guid.Parse(key), actor);
        }

        Assert.Contains(rows, row => row.Actor == userA);
        Assert.Contains(rows, row => row.Actor == userB);
    }

    private static async Task<HttpStatusCode> SetCultureAsync(IdentityClient client, string culture, CancellationToken ct)
    {
        using var response = await client.SendUnsafeAsync(HttpMethod.Put, PreferencesUri, JsonContent.Create(new { culture }), ct);
        return response.StatusCode;
    }

    /// <summary>User update rows written by the preference changes: the subject key and the recorded actor.</summary>
    private async Task<List<(string Key, Guid? Actor)>> ActorsAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "select entity_key, actor_user_id from audit_entries where entity_type = 'User' and action = 'update' and changes ? 'PreferredCulture'",
            connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<(string, Guid?)>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add((reader.GetString(0), await reader.IsDBNullAsync(1, ct) ? null : reader.GetGuid(1)));
        }

        return rows;
    }
}