using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Account.Sessions;

/// <summary>A security stamp change ends the other sessions on their very next request; the acting session stays signed in.</summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class ImmediateRevocationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri MeUri = new("/api/identity/account/me", UriKind.Relative);
    private static readonly Uri PasskeysUri = new("/api/identity/account/passkeys", UriKind.Relative);
    private static readonly Uri BeginAddUri = new("/api/identity/account/passkeys/options", UriKind.Relative);
    private static readonly Uri RecoveryOptionsUri = new("/api/identity/auth/registration/options", UriKind.Relative);
    private static readonly Uri RegistrationUri = new("/api/identity/auth/registration", UriKind.Relative);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task PasskeyRemoval_RejectsTheOtherClientOnItsNextRequest_AndKeepsTheActingClient()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var acting = new IdentityClient(_api);
        using var first = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, acting, first, "revoke-passkey@aspireboot.test", ct);
        await acting.RefreshAntiforgeryTokenAsync(ct);

        using var other = new IdentityClient(_api);
        using (var signIn = await IdentityFlow.SignInAsync(other, first, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        await AssertStatusAsync(other, HttpStatusCode.OK, ct);

        using var second = new SoftwareAuthenticator();
        using (var added = await AddPasskeyAsync(acting, second, "Notebook", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        // Adding a passkey does not change the stamp: the other session is still valid.
        await AssertStatusAsync(other, HttpStatusCode.OK, ct);

        var notebookId = await PasskeyIdAsync(acting, "Notebook", ct);
        using (var removed = await acting.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(notebookId), null, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        await AssertStatusAsync(other, HttpStatusCode.Unauthorized, ct);
        await AssertStatusAsync(acting, HttpStatusCode.OK, ct);
    }

    [Fact]
    public async Task Recovery_RejectsTheOtherClientOnItsNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string email = "revoke-recovery@aspireboot.test";
        using var owner = new IdentityClient(_api);
        using var passkey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, owner, passkey, email, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await AssertStatusAsync(owner, HttpStatusCode.OK, ct);

        using var recovering = new IdentityClient(_api);
        using (var request = await IdentityFlow.RequestRecoveryAsync(recovering, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        }

        var link = IdentityFlow.ParseRecoveryLink((await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct)).Text);
        using var recovered = new SoftwareAuthenticator();
        using (var complete = await CompleteRecoveryAsync(recovering, link, recovered, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        }

        await AssertStatusAsync(owner, HttpStatusCode.Unauthorized, ct);
        await AssertStatusAsync(recovering, HttpStatusCode.OK, ct);
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

    internal static async Task AssertStatusAsync(IdentityClient client, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.Http.GetAsync(MeUri, ct);
        Assert.Equal(expected, response.StatusCode);
    }

    internal static async Task<string> PasskeyIdAsync(IdentityClient client, string name, CancellationToken ct)
    {
        using var response = await client.Http.GetAsync(PasskeysUri, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var passkeys = await response.Content.ReadFromJsonAsync<JsonElement[]>(ct) ?? [];
        return passkeys.Single(passkey => passkey.GetProperty("name").GetString() == name).GetProperty("id").GetString()!;
    }

    internal static async Task<HttpResponseMessage> AddPasskeyAsync(
        IdentityClient client, SoftwareAuthenticator authenticator, string name, CancellationToken ct)
    {
        using var options = await client.SendUnsafeAsync(HttpMethod.Post, BeginAddUri, null, ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var creationOptions = await options.Content.ReadAsStringAsync(ct);

        return await client.SendUnsafeAsync(HttpMethod.Post, PasskeysUri, JsonContent.Create(new
        {
            credential = JsonDocument.Parse(authenticator.CreateAttestation(creationOptions)).RootElement,
            name,
        }), ct);
    }

    private static async Task<HttpResponseMessage> CompleteRecoveryAsync(
        IdentityClient client, (string UserId, string Token) link, SoftwareAuthenticator authenticator, CancellationToken ct)
    {
        using var options = await client.SendUnsafeAsync(
            HttpMethod.Post, RecoveryOptionsUri, JsonContent.Create(new { userId = link.UserId, token = link.Token, purpose = "recover" }), ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var creationOptions = await options.Content.ReadAsStringAsync(ct);

        return await client.SendUnsafeAsync(HttpMethod.Post, RegistrationUri, JsonContent.Create(new
        {
            userId = link.UserId,
            token = link.Token,
            purpose = "recover",
            credential = JsonDocument.Parse(authenticator.CreateAttestation(creationOptions)).RootElement,
            name = "Novo celular",
        }), ct);
    }
}