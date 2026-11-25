using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Auth.SignIn;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class SignInTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string SessionCookiePrefix = "__Host-aspireboot=";
    private static readonly Uri OptionsUri = new("/api/identity/auth/signin/options", UriKind.Relative);
    private static readonly Uri SignInUri = new("/api/identity/auth/signin", UriKind.Relative);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task RegisteredPasskey_SignsBackIn_AfterSignOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "signin-roundtrip@aspireboot.test", ct);
        Assert.Equal(HttpStatusCode.NoContent, await IdentityFlow.SignOutAsync(client, ct));

        using var response = await IdentityFlow.SignInAsync(client, authenticator, keepSignedIn: false, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(FindSessionCookie(response));
    }

    [Fact]
    public async Task KeepSignedInTrue_SetsPersistentSessionCookie()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "signin-persist@aspireboot.test", ct);
        await IdentityFlow.SignOutAsync(client, ct);

        using var response = await IdentityFlow.SignInAsync(client, authenticator, keepSignedIn: true, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = FindSessionCookie(response);
        Assert.NotNull(cookie);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KeepSignedInFalse_SetsSessionCookieWithoutExpiry()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "signin-session@aspireboot.test", ct);
        await IdentityFlow.SignOutAsync(client, ct);

        using var response = await IdentityFlow.SignInAsync(client, authenticator, keepSignedIn: false, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = FindSessionCookie(response);
        Assert.NotNull(cookie);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BadCredential_Returns401GenericSignInFailed_AndSetsNoSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "signin-badcred@aspireboot.test", ct);
        await IdentityFlow.SignOutAsync(client, ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        using var options = await client.SendUnsafeAsync(HttpMethod.Post, OptionsUri, null, ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var garbage = JsonDocument.Parse("""{"id":"abc","rawId":"abc","type":"public-key","response":{"clientDataJSON":"e30","authenticatorData":"e30","signature":"e30"}}""");

        using var response = await client.SendUnsafeAsync(HttpMethod.Post, SignInUri, JsonContent.Create(new
        {
            credential = garbage.RootElement,
            keepSignedIn = true,
        }), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("identity.signInFailed", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Null(FindSessionCookie(response));
    }

    [Fact]
    public async Task MissingCredential_Returns401GenericSignInFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var response = await client.SendUnsafeAsync(HttpMethod.Post, SignInUri, JsonContent.Create(new { keepSignedIn = false }), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("identity.signInFailed", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
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

    private static string? FindSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        return cookies.FirstOrDefault(cookie => cookie.StartsWith(SessionCookiePrefix, StringComparison.Ordinal));
    }
}