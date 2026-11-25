using System.Net;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Account.Sessions;

/// <summary>
/// Two separate API instances (two factories) share Redis and Postgres, like two replicas. A stamp change made through
/// instance A must reject the session cookie on instance B on its next request: HybridCache's local cache is not
/// invalidated across instances, so the stamp entry bypasses it.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class CrossInstanceRevocationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootApiFactory _instanceA = new(infrastructure);
    private readonly AspireBootApiFactory _instanceB = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task StampChangeOnInstanceA_RejectsTheSessionOnInstanceB_OnItsNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var onA = new IdentityClient(_instanceA);
        using var first = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, onA, first, "cross-instance@aspireboot.test", ct);
        await onA.RefreshAntiforgeryTokenAsync(ct);

        using var onB = new IdentityClient(_instanceB);
        using (var signIn = await IdentityFlow.SignInAsync(onB, first, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        // Warms the stamp on instance B (and would warm its local cache, if it were not bypassed).
        await ImmediateRevocationTests.AssertStatusAsync(onB, HttpStatusCode.OK, ct);
        await ImmediateRevocationTests.AssertStatusAsync(onB, HttpStatusCode.OK, ct);

        using var second = new SoftwareAuthenticator();
        using (var added = await ImmediateRevocationTests.AddPasskeyAsync(onA, second, "Notebook", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        var notebookId = await ImmediateRevocationTests.PasskeyIdAsync(onA, "Notebook", ct);
        using (var removed = await onA.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(notebookId), null, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

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