using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Account.UpdateProfile;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class UpdateProfileTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri ProfileUri = new("/api/identity/account/profile", UriKind.Relative);
    private static readonly Uri MeUri = new("/api/identity/account/me", UriKind.Relative);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task SignedIn_RenamesTheUser_AndGetMeReflectsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "profile-rename@aspireboot.test", ct);
        // The antiforgery token is bound to the identity, so refresh it once the account is signed in.
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var update = await client.SendUnsafeAsync(
            HttpMethod.Put, ProfileUri, JsonContent.Create(new { displayName = "  Beatriz Souza  " }), ct);
        using var me = await client.Http.GetAsync(MeUri, ct);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<ProfileBody>(ct);
        Assert.Equal("Beatriz Souza", profile?.DisplayName);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Ana\u0007")]
    public async Task InvalidDisplayName_Returns400WithDisplayNameError_AndKeepsTheName(string displayName)
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "profile-invalid@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var update = await client.SendUnsafeAsync(
            HttpMethod.Put, ProfileUri, JsonContent.Create(new { displayName }), ct);
        using var me = await client.Http.GetAsync(MeUri, ct);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        using var problem = JsonDocument.Parse(await update.Content.ReadAsStringAsync(ct));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("displayName", out _));
        var profile = await me.Content.ReadFromJsonAsync<ProfileBody>(ct);
        Assert.Equal("Ana", profile?.DisplayName);
    }

    [Fact]
    public async Task TooLongDisplayName_Returns400WithDisplayNameError()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "profile-long@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var update = await client.SendUnsafeAsync(
            HttpMethod.Put, ProfileUri, JsonContent.Create(new { displayName = new string('a', 101) }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        using var problem = JsonDocument.Parse(await update.Content.ReadAsStringAsync(ct));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("displayName", out _));
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);

        using var update = await client.SendUnsafeAsync(
            HttpMethod.Put, ProfileUri, JsonContent.Create(new { displayName = "Beatriz" }), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, update.StatusCode);
    }

    [Fact]
    public async Task MissingAntiforgeryToken_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "profile-xsrf@aspireboot.test", ct);

        using var update = await client.Http.PutAsJsonAsync(ProfileUri, new { displayName = "Beatriz" }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
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