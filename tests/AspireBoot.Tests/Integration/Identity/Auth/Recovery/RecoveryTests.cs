using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Core.Identity.Domain;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Identity.Auth.Recovery;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class RecoveryTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri OptionsUri = new("/api/identity/auth/registration/options", UriKind.Relative);
    private static readonly Uri RegistrationUri = new("/api/identity/auth/registration", UriKind.Relative);
    private static readonly Uri MeUri = new("/api/identity/account/me", UriKind.Relative);

    private readonly StampValidatingApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task UnknownUnconfirmedAndConfirmedEmails_GetIdenticalAcceptedResponses_OnlyConfirmedGetsEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string unknown = "recover-unknown@aspireboot.test";
        const string unconfirmed = "recover-unconfirmed@aspireboot.test";
        const string confirmed = "recover-confirmed@aspireboot.test";

        using var signUp = new IdentityClient(_api);
        await IdentityFlow.SignUpAsync(signUp, unconfirmed, ct);
        await infrastructure.Mailpit.WaitForMessageAsync(unconfirmed, MailTimeout, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);

        using var authenticator = new SoftwareAuthenticator();
        using var owner = new IdentityClient(_api);
        await IdentityFlow.RegisterAsync(infrastructure, owner, authenticator, confirmed, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);

        using var anonymous = new IdentityClient(_api);
        using var unknownResponse = await IdentityFlow.RequestRecoveryAsync(anonymous, unknown, ct);
        using var unconfirmedResponse = await IdentityFlow.RequestRecoveryAsync(anonymous, unconfirmed, ct);
        using var confirmedResponse = await IdentityFlow.RequestRecoveryAsync(anonymous, confirmed, ct);

        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unconfirmedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, confirmedResponse.StatusCode);
        Assert.Equal(string.Empty, await unknownResponse.Content.ReadAsStringAsync(ct));
        Assert.Equal(string.Empty, await unconfirmedResponse.Content.ReadAsStringAsync(ct));
        Assert.Equal(string.Empty, await confirmedResponse.Content.ReadAsStringAsync(ct));

        var received = await infrastructure.Mailpit.WaitForMessageAsync(confirmed, MailTimeout, ct);
        Assert.Contains("https://localhost/recover/complete?u=", received.Text, StringComparison.Ordinal);
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        Assert.Empty(await infrastructure.Mailpit.SearchAsync(unknown, ct));
        Assert.Empty(await infrastructure.Mailpit.SearchAsync(unconfirmed, ct));
    }

    [Fact]
    public async Task FullRecovery_RegistersNewPasskey_EndsOtherSessions_AndLinkIsSingleUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string email = "recover-full@aspireboot.test";

        using var sessionA = new IdentityClient(_api);
        using var passkeyA = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, sessionA, passkeyA, email, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);

        using (var me = await sessionA.Http.GetAsync(MeUri, ct))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        using var anonymous = new IdentityClient(_api);
        using (var request = await IdentityFlow.RequestRecoveryAsync(anonymous, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        }

        var link = IdentityFlow.ParseRecoveryLink((await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct)).Text);

        using var passkeyB = new SoftwareAuthenticator();
        using (var complete = await CompleteRecoveryAsync(anonymous, link, passkeyB, "Novo celular", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        }

        using (var oldSession = await sessionA.Http.GetAsync(MeUri, ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        }

        using (var signIn = await IdentityFlow.SignInAsync(anonymous, passkeyB, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        using var newSession = await anonymous.Http.GetAsync(MeUri, ct);
        Assert.Equal(HttpStatusCode.OK, newSession.StatusCode);

        using var reused = await PostOptionsAsync(anonymous, link, ct);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Contains("identity.invalidLink", await reused.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredRecoveryLink_Returns400InvalidLink()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string email = "recover-expired@aspireboot.test";
        using var owner = new IdentityClient(_api);
        using var passkeyA = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, owner, passkeyA, email, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);

        await using var expiring = new AspireBootExpiringApiFactory(infrastructure);
        using var anonymous = new IdentityClient(expiring);
        using (var request = await IdentityFlow.RequestRecoveryAsync(anonymous, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        }

        var link = IdentityFlow.ParseRecoveryLink((await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct)).Text);
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        using var response = await PostOptionsAsync(anonymous, link, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("identity.invalidLink", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedEmail_Returns400ProblemDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new IdentityClient(_api);

        using var response = await IdentityFlow.RequestRecoveryAsync(client, "not-an-email", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static Task<HttpResponseMessage> PostOptionsAsync(IdentityClient client, (string UserId, string Token) link, CancellationToken ct) =>
        client.SendUnsafeAsync(HttpMethod.Post, OptionsUri, JsonContent.Create(new { userId = link.UserId, token = link.Token, purpose = "recover" }), ct);

    private static async Task<HttpResponseMessage> CompleteRecoveryAsync(
        IdentityClient client, (string UserId, string Token) link, SoftwareAuthenticator authenticator, string name, CancellationToken ct)
    {
        using var options = await PostOptionsAsync(client, link, ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var creationOptions = await options.Content.ReadAsStringAsync(ct);

        return await client.SendUnsafeAsync(HttpMethod.Post, RegistrationUri, JsonContent.Create(new
        {
            userId = link.UserId,
            token = link.Token,
            purpose = "recover",
            credential = JsonDocument.Parse(authenticator.CreateAttestation(creationOptions)).RootElement,
            name,
        }), ct);
    }

    /// <summary>
    /// Validates the security stamp on every request, so a stamp change ends a session at once. Only recovery needs this:
    /// the shared factory keeps the production interval, because passkey removal also changes the stamp.
    /// </summary>
    private sealed class StampValidatingApiFactory(InfrastructureFixture infrastructure) : AspireBootApiFactory(infrastructure)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero));
        }
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
}