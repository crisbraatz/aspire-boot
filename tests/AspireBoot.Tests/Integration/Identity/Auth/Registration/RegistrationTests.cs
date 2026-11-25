using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using AspireBoot.Core.Identity.Domain;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Identity.Auth.Registration;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class RegistrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri SignUpUri = new("/api/identity/auth/signup", UriKind.Relative);
    private static readonly Uri OptionsUri = new("/api/identity/auth/registration/options", UriKind.Relative);
    private static readonly Uri RegistrationUri = new("/api/identity/auth/registration", UriKind.Relative);
    private static readonly Regex ConfirmationLink = new(
        @"https://localhost/confirm\?u=(?<user>[0-9a-fA-F-]{36})&t=(?<token>[A-Za-z0-9_-]+)",
        RegexOptions.CultureInvariant);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task ConfirmationLink_RegistersFirstPasskey_ConfirmsEmail_AndSignsIn()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        var link = await SignUpAsync(client, "reg-happy@aspireboot.test", ct);

        var options = await BeginAsync(client, link, "confirm", ct);
        using var response = await CompleteAsync(client, link, "confirm", authenticator.CreateAttestation(options), "Meu celular", ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith("__Host-aspireboot=", StringComparison.Ordinal)),
            "Completing the registration must sign the user in.");

        await using var scope = _api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(link.UserId.ToString("D"));
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        var passkeys = await users.GetPasskeysAsync(user);
        Assert.Single(passkeys);
        Assert.Equal("Meu celular", passkeys[0].Name);
    }

    [Fact]
    public async Task ReusedLink_Returns400InvalidLink()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        var link = await SignUpAsync(client, "reg-reuse@aspireboot.test", ct);
        var options = await BeginAsync(client, link, "confirm", ct);
        using (var first = await CompleteAsync(client, link, "confirm", authenticator.CreateAttestation(options), "Celular", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        // The antiforgery token is bound to the identity, so the client needs a fresh one after signing in.
        await client.RefreshAntiforgeryTokenAsync(ct);
        using var second = await PostAsync(client, OptionsUri, new { userId = link.UserId, token = link.Token, purpose = "confirm" }, ct);

        await AssertInvalidLinkAsync(second, ct);
    }

    [Fact]
    public async Task TamperedToken_Returns400InvalidLink()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        var link = await SignUpAsync(client, "reg-tamper@aspireboot.test", ct);
        var tamperedToken = Flip(link.Token);

        using var response = await PostAsync(client, OptionsUri, new { userId = link.UserId, token = tamperedToken, purpose = "confirm" }, ct);

        await AssertInvalidLinkAsync(response, ct);
    }

    [Fact]
    public async Task ExpiredLink_Returns400InvalidLink()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await using var expiring = new AspireBootExpiringApiFactory(infrastructure);
        using var client = new IdentityClient(expiring);
        var link = await SignUpAsync(client, "reg-expired@aspireboot.test", ct);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        using var response = await PostAsync(client, OptionsUri, new { userId = link.UserId, token = link.Token, purpose = "confirm" }, ct);

        await AssertInvalidLinkAsync(response, ct);
    }

    [Fact]
    public async Task BadCredential_Returns400PasskeyFailed_AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        var link = await SignUpAsync(client, "reg-badcred@aspireboot.test", ct);
        await BeginAsync(client, link, "confirm", ct);
        using var garbage = JsonDocument.Parse("""{"id":"abc","rawId":"abc","type":"public-key","response":{"clientDataJSON":"e30","attestationObject":"e30"}}""");

        using var response = await PostAsync(client, RegistrationUri, new
        {
            userId = link.UserId,
            token = link.Token,
            purpose = "confirm",
            credential = garbage.RootElement,
            name = "Celular",
        }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("identity.passkeyFailed", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

        await using var scope = _api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(link.UserId.ToString("D"));
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
        Assert.Empty(await users.GetPasskeysAsync(user));
    }

    [Fact]
    public async Task RecoverPurpose_ForUnconfirmedUser_Returns400InvalidLink()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        var link = await SignUpAsync(client, "reg-recover@aspireboot.test", ct);

        using var response = await PostAsync(client, OptionsUri, new { userId = link.UserId, token = link.Token, purpose = "recover" }, ct);

        await AssertInvalidLinkAsync(response, ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("reset")]
    [InlineData("CONFIRM")]
    public async Task UnknownPurpose_Returns400ValidationProblem_WithPurposeError(string purpose)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new IdentityClient(_api);
        using var attestation = JsonDocument.Parse("{}");

        using var response = await CompleteAsync(client, new SignUpLink(Guid.NewGuid(), "not-a-real-token"), purpose, attestation.RootElement.GetRawText(), "Celular", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("purpose", out _), "The purpose must be reported as a validation error.");
    }

    [Fact]
    public async Task OversizedBody_Returns413_BeforeTheCredentialIsParsed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new IdentityClient(_api);
        var padding = new string('a', 40 * 1024);
        var body = $"{{\"userId\":\"{Guid.NewGuid():D}\",\"token\":\"x\",\"purpose\":\"confirm\",\"credential\":{{}},\"name\":\"{padding}\"}}";

        using var response = await client.SendUnsafeAsync(HttpMethod.Post, RegistrationUri, new StringContent(body, Encoding.UTF8, "application/json"), ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static async Task AssertInvalidLinkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("identity.invalidLink", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    private static string Flip(string encoded)
    {
        var chars = encoded.ToCharArray();
        chars[^1] = chars[^1] == 'A' ? 'B' : 'A';
        return new string(chars);
    }

    private static async Task<string> BeginAsync(IdentityClient client, SignUpLink link, string purpose, CancellationToken ct)
    {
        using var response = await PostAsync(client, OptionsUri, new { userId = link.UserId, token = link.Token, purpose }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static Task<HttpResponseMessage> CompleteAsync(
        IdentityClient client, SignUpLink link, string purpose, string attestationJson, string name, CancellationToken ct) =>
        PostAsync(client, RegistrationUri, new
        {
            userId = link.UserId,
            token = link.Token,
            purpose,
            credential = JsonDocument.Parse(attestationJson).RootElement,
            name,
        }, ct);

    private static Task<HttpResponseMessage> PostAsync(IdentityClient client, Uri uri, object body, CancellationToken ct) =>
        client.SendUnsafeAsync(HttpMethod.Post, uri, JsonContent.Create(body), ct);

    /// <summary>Signs up through the real endpoint and reads the confirmation link from Mailpit.</summary>
    private async Task<SignUpLink> SignUpAsync(IdentityClient client, string email, CancellationToken ct)
    {
        using var response = await PostAsync(client, SignUpUri, new { displayName = "Ana", email, culture = "pt-BR" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        var match = ConfirmationLink.Match(received.Text);
        Assert.True(match.Success, "The confirmation email must carry the confirmation link.");

        return new SignUpLink(Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
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

    private sealed record SignUpLink(Guid UserId, string Token);
}