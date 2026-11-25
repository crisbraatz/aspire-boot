using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;

using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Infrastructure.Email.Templates;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Identity.Auth.SignUp;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class SignUpTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri SignUpUri = new("/api/identity/auth/signup", UriKind.Relative);
    private static readonly Regex ConfirmationLink = new(
        @"https://localhost/confirm\?u=(?<user>[0-9a-fA-F-]{36})&t=(?<token>[A-Za-z0-9_-]+)",
        RegexOptions.CultureInvariant);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task NewEmail_Returns202WithEmptyBody_CreatesPasswordlessUser_AndSendsLocalizedEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "signup-new@aspireboot.test";
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        using var client = new IdentityClient(_api);

        using var response = await PostSignUpAsync(client, new { displayName = "Ana", email, culture = "pt-BR" }, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(ct));

        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        var expected = EmailTemplateCatalog.Render(EmailTemplateKind.ConfirmationEmail, "pt-BR",
            new EmailTemplateModel("Ana", "https://localhost/confirm"));
        Assert.Equal(expected.Subject, received.Subject);

        var match = ConfirmationLink.Match(received.Text);
        Assert.True(match.Success, "The confirmation email must carry the confirmation link.");

        await using var scope = _api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Equal(user.Id.ToString("D"), match.Groups["user"].Value.ToLowerInvariant());
        Assert.Equal("Ana", user.DisplayName.Value);
        Assert.Equal("pt-BR", user.PreferredCulture.Value);
        Assert.Null(user.PasswordHash);
        Assert.True(await users.IsInRoleAsync(user, Roles.User));

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(match.Groups["token"].Value));
        Assert.True((await users.ConfirmEmailAsync(user, token)).Succeeded, "The link must carry a valid confirmation token.");
    }

    [Fact]
    public async Task ExistingEmail_Returns202WithSameEmptyBody_AndSendsNoEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "signup-existing@aspireboot.test";
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        using var client = new IdentityClient(_api);
        using (var first = await PostSignUpAsync(client, new { displayName = "Ana", email, culture = "pt-BR" }, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        }

        await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);

        using var second = await PostSignUpAsync(client, new { displayName = "Outra Pessoa", email, culture = "en-US" }, ct);

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(string.Empty, await second.Content.ReadAsStringAsync(ct));
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        Assert.Empty(await infrastructure.Mailpit.SearchAsync(email, ct));
    }

    [Theory]
    [InlineData("A", "ana@aspireboot.test", "pt-BR")]
    [InlineData("Ana", "not-an-email", "pt-BR")]
    [InlineData("Ana", "ana@aspireboot.test", "fr-FR")]
    [InlineData("Ana", "ana@aspireboot.test", null)]
    public async Task InvalidInput_Returns400ProblemDetails(string displayName, string email, string? culture)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new IdentityClient(_api);

        using var response = await PostSignUpAsync(client, new { displayName, email, culture }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostWithoutAntiforgeryToken_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var http = _api.CreateHttpsClient();

        using var response = await http.PostAsJsonAsync(
            SignUpUri,
            new { displayName = "Ana", email = "no-token@aspireboot.test", culture = "pt-BR" },
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("antiforgery.invalid", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostWithAntiforgeryToken_IsAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var response = await PostSignUpAsync(client, new { displayName = "Ana", email = "with-token@aspireboot.test", culture = "en-US" }, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task ExceedingTheSignupRateLimit_Returns429()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var limited = new AspireBootApiFactory(infrastructure, new Dictionary<string, string?>
        {
            ["RateLimiting:signup:PermitLimit"] = "2",
        });
        using var client = new IdentityClient(limited);

        using var first = await PostSignUpAsync(client, new { displayName = "Ana", email = "rl-1@aspireboot.test", culture = "pt-BR" }, ct);
        using var second = await PostSignUpAsync(client, new { displayName = "Ana", email = "rl-2@aspireboot.test", culture = "pt-BR" }, ct);
        using var third = await PostSignUpAsync(client, new { displayName = "Ana", email = "rl-3@aspireboot.test", culture = "pt-BR" }, ct);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.Contains("Retry-After"));
    }

    private static Task<HttpResponseMessage> PostSignUpAsync(IdentityClient client, object body, CancellationToken ct) =>
        client.SendUnsafeAsync(HttpMethod.Post, SignUpUri, JsonContent.Create(body), ct);

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