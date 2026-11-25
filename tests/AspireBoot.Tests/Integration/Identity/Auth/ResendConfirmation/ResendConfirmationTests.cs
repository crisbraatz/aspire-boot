using System.Net;
using System.Net.Http.Json;

using AspireBoot.Core.Identity.Domain;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Identity.Auth.ResendConfirmation;

[Trait("Category", "Integration")]
public sealed class ResendConfirmationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri ResendUri = new("/api/identity/auth/confirmation/resend", UriKind.Relative);
    private static readonly Uri SignUpUri = new("/api/identity/auth/signup", UriKind.Relative);

    // The test factory sets Identity:EmailCooldownSeconds to 1. Cooldown uses the real cache clock, so tests wait past it.
    private static readonly TimeSpan CooldownWait = TimeSpan.FromSeconds(1.5);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task UnconfirmedAccount_Returns202AndSendsNewConfirmationEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "resend-unconfirmed@aspireboot.test";
        using var client = await SignUpAndWaitForEmailAsync(email, ct);

        using var response = await client.SendUnsafeAsync(HttpMethod.Post, ResendUri, JsonContent.Create(new { email }), ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(ct));
        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        Assert.Contains("https://localhost/confirm?u=", received.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResendWithinCooldown_SendsNothing_AndAfterCooldown_SendsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "resend-cooldown@aspireboot.test";
        using var client = await SignUpAndWaitForEmailAsync(email, ct);

        using (var first = await PostResendAsync(client, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        }

        await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);

        using (var within = await PostResendAsync(client, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, within.StatusCode);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        Assert.Empty(await infrastructure.Mailpit.SearchAsync(email, ct));

        await Task.Delay(CooldownWait, ct);
        using (var after = await PostResendAsync(client, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, after.StatusCode);
        }

        await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
    }

    [Fact]
    public async Task UnknownEmail_Returns202_AndSendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "resend-unknown@aspireboot.test";
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        using var client = new IdentityClient(_api);

        using var response = await PostResendAsync(client, email, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(ct));
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        Assert.Empty(await infrastructure.Mailpit.SearchAsync(email, ct));
    }

    [Fact]
    public async Task ConfirmedAccount_Returns202_AndSendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "resend-confirmed@aspireboot.test";
        using var client = await SignUpAndWaitForEmailAsync(email, ct);

        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("The signup did not create the account.");
            await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user));
        }

        await infrastructure.Mailpit.DeleteAllAsync(ct);
        using var response = await PostResendAsync(client, email, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        Assert.Empty(await infrastructure.Mailpit.SearchAsync(email, ct));
    }

    [Fact]
    public async Task InvalidEmail_Returns400ProblemDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new IdentityClient(_api);

        using var response = await PostResendAsync(client, "not-an-email", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostWithoutAntiforgeryToken_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var http = _api.CreateHttpsClient();

        using var response = await http.PostAsJsonAsync(ResendUri, new { email = "resend-no-token@aspireboot.test" }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Creates an unconfirmed account through the public signup and waits until its confirmation email is delivered.</summary>
    private async Task<IdentityClient> SignUpAndWaitForEmailAsync(string email, CancellationToken ct)
    {
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        var client = new IdentityClient(_api);

        using var response = await client.SendUnsafeAsync(
            HttpMethod.Post,
            SignUpUri,
            JsonContent.Create(new { displayName = "Ana", email, culture = "en-US" }),
            ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        return client;
    }

    private static Task<HttpResponseMessage> PostResendAsync(IdentityClient client, string email, CancellationToken ct) =>
        client.SendUnsafeAsync(HttpMethod.Post, ResendUri, JsonContent.Create(new { email }), ct);

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