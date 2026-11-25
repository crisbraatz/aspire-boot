using System.Net;
using System.Net.Http.Json;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Account.UpdatePreferences;

[Trait("Category", "Integration")]
public sealed class UpdatePreferencesTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri PreferencesUri = new("/api/identity/account/preferences", UriKind.Relative);
    private static readonly Uri MeUri = new("/api/identity/account/me", UriKind.Relative);
    private static readonly Uri RecoveryUri = new("/api/identity/auth/recovery", UriKind.Relative);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task SignedIn_SavesCulture_AndLaterEmailsUseIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        const string email = "prefs-saved@aspireboot.test";
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, email, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        // The antiforgery token is bound to the identity, so refresh it once the account is signed in.
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var update = await client.SendUnsafeAsync(
            HttpMethod.Put, PreferencesUri, JsonContent.Create(new { culture = "en-US" }), ct);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        using var me = await client.Http.GetAsync(MeUri, ct);
        var profile = await me.Content.ReadFromJsonAsync<ProfileBody>(ct);
        Assert.Equal("en-US", profile?.Culture);

        using var anonymous = new IdentityClient(_api);
        using var recovery = await anonymous.SendUnsafeAsync(
            HttpMethod.Post, RecoveryUri, JsonContent.Create(new { email }), ct);
        Assert.Equal(HttpStatusCode.Accepted, recovery.StatusCode);

        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        Assert.Equal("Recover access to your AspireBoot account", received.Subject);
    }

    [Fact]
    public async Task UnsupportedCulture_Returns400_AndKeepsTheStoredCulture()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "prefs-invalid@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var update = await client.SendUnsafeAsync(
            HttpMethod.Put, PreferencesUri, JsonContent.Create(new { culture = "fr-FR" }), ct);
        using var me = await client.Http.GetAsync(MeUri, ct);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<ProfileBody>(ct);
        Assert.Equal("pt-BR", profile?.Culture);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);

        using var update = await client.SendUnsafeAsync(
            HttpMethod.Put, PreferencesUri, JsonContent.Create(new { culture = "en-US" }), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, update.StatusCode);
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

    private sealed record ProfileBody(Guid Id, string DisplayName, string Email, string Culture);
}