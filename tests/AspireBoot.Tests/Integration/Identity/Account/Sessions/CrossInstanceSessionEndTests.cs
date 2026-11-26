using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Account.Sessions;

/// <summary>Ending a session through instance A rejects its cookie on instance B (shared Redis and Postgres) on the next request.</summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class CrossInstanceSessionEndTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri SessionsUri = new("/api/identity/account/sessions", UriKind.Relative);

    private readonly AspireBootApiFactory _instanceA = new(infrastructure);
    private readonly AspireBootApiFactory _instanceB = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task EndingASessionOnInstanceA_RejectsItOnInstanceB_OnItsNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var onA = new IdentityClient(_instanceA);
        using var passkey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, onA, passkey, "cross-end@aspireboot.test", ct);
        await onA.RefreshAntiforgeryTokenAsync(ct);

        using var onB = new IdentityClient(_instanceB);
        using (var signIn = await IdentityFlow.SignInAsync(onB, passkey, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        // Warms the session entry on instance B.
        await ImmediateRevocationTests.AssertStatusAsync(onB, HttpStatusCode.OK, ct);
        await ImmediateRevocationTests.AssertStatusAsync(onB, HttpStatusCode.OK, ct);

        using var list = await onA.Http.GetAsync(SessionsUri, ct);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement[]>(ct))!;
        var otherId = items.Single(item => !item.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid();

        using var ended = await onA.SendUnsafeAsync(HttpMethod.Delete, new Uri($"{SessionsUri}/{otherId:D}", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        await ImmediateRevocationTests.AssertStatusAsync(onB, HttpStatusCode.Unauthorized, ct);
        await ImmediateRevocationTests.AssertStatusAsync(onA, HttpStatusCode.OK, ct);
    }

    public ValueTask InitializeAsync()
    {
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _instanceA.DisposeAsync();
        await _instanceB.DisposeAsync();
    }
}