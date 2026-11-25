using System.Net;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Account.GetMe;

[Trait("Category", "Integration")]
public sealed class GetMeTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri MeUri = new("/api/identity/account/me", UriKind.Relative);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);

        using var response = await client.Http.GetAsync(MeUri, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignedIn_ReturnsOwnProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "me-profile@aspireboot.test", ct);
        await IdentityFlow.SignOutAsync(client, ct);
        using (var signIn = await IdentityFlow.SignInAsync(client, authenticator, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        using var response = await client.Http.GetAsync(MeUri, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>(ct);
        Assert.NotNull(me);
        Assert.Equal(userId, me.Id);
        Assert.Equal("Ana", me.DisplayName);
        Assert.Equal("me-profile@aspireboot.test", me.Email);
        Assert.Equal("pt-BR", me.Culture);
    }

    [Fact]
    public async Task SignOut_EndsTheSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "me-signout@aspireboot.test", ct);

        var signOut = await IdentityFlow.SignOutAsync(client, ct);
        using var response = await client.Http.GetAsync(MeUri, ct);

        Assert.Equal(HttpStatusCode.NoContent, signOut);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public ValueTask InitializeAsync()
    {
        // Touching Services starts the Worker host, which sends the queued emails.
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private sealed record MeBody(Guid Id, string DisplayName, string Email, string Culture);
}