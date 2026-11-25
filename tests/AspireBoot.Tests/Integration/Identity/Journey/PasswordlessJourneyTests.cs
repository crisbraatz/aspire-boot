using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Identity.Journey;

/// <summary>
/// The whole passwordless journey through the real HTTP API, Worker, Mailpit and the software authenticator:
/// sign-up in pt-BR, confirmation with the first passkey, language change, a second passkey, sign-out and sign-in,
/// removal rules, recovery in es-ES and the end of the old session.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PasswordlessJourneyTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly Uri MeUri = new("/api/identity/account/me", UriKind.Relative);
    private static readonly Uri PreferencesUri = new("/api/identity/account/preferences", UriKind.Relative);
    private static readonly Uri PasskeysUri = new("/api/identity/account/passkeys", UriKind.Relative);
    private static readonly Uri AddPasskeyOptionsUri = new("/api/identity/account/passkeys/options", UriKind.Relative);
    private static readonly Uri RegistrationOptionsUri = new("/api/identity/auth/registration/options", UriKind.Relative);
    private static readonly Uri RegistrationUri = new("/api/identity/auth/registration", UriKind.Relative);

    // Subjects per culture, from IdentityEmailTemplates.
    private const string PtBrConfirmationSubject = "Confirme sua conta no AspireBoot";
    private const string EsPasskeyAddedSubject = "Se añadió una clave de acceso a tu cuenta de AspireBoot";
    private const string EsPasskeyRemovedSubject = "Se eliminó una clave de acceso de tu cuenta de AspireBoot";
    private const string EsRecoverySubject = "Recupera el acceso a tu cuenta de AspireBoot";

    private readonly StampValidatingApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task PasswordlessJourney_FollowsThePreferredLanguage_AndStoresNoPasswords()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        const string email = "journey@aspireboot.test";
        using var client = new IdentityClient(_api);
        using var firstPasskey = new SoftwareAuthenticator();

        // Sign-up in pt-BR; the confirmation email is in Portuguese and confirms with the first passkey.
        await IdentityFlow.RegisterAsync(infrastructure, client, firstPasskey, email, ct);
        await WaitForSubjectAsync(email, PtBrConfirmationSubject, null, ct);
        await client.RefreshAntiforgeryTokenAsync(ct);

        var profile = await GetProfileAsync(client, ct);
        Assert.Equal("pt-BR", profile.Culture);
        Assert.Equal(email, profile.Email);

        // Save es-ES as the preference, then add a second passkey: its notification is in Spanish.
        using (var prefs = await client.SendUnsafeAsync(
            HttpMethod.Put, PreferencesUri, JsonContent.Create(new { culture = "es-ES" }), ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, prefs.StatusCode);
        }

        Assert.Equal("es-ES", (await GetProfileAsync(client, ct)).Culture);

        using var notebookPasskey = new SoftwareAuthenticator();
        using (var added = await AddPasskeyAsync(client, notebookPasskey, "Notebook", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        var addedEmail = await WaitForSubjectAsync(email, EsPasskeyAddedSubject, "Notebook", ct);
        Assert.Contains("Notebook", addedEmail.Text, StringComparison.Ordinal);
        Assert.Equal(2, (await ListPasskeysAsync(client, ct)).Length);

        // Sign out, then sign in again and keep the session.
        Assert.Equal(HttpStatusCode.NoContent, await IdentityFlow.SignOutAsync(client, ct));
        using (var afterSignOut = await client.Http.GetAsync(MeUri, ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, afterSignOut.StatusCode);
        }

        using (var signIn = await IdentityFlow.SignInAsync(client, firstPasskey, keepSignedIn: true, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        await client.RefreshAntiforgeryTokenAsync(ct);
        Assert.Equal("es-ES", (await GetProfileAsync(client, ct)).Culture);

        // Remove the Notebook passkey (Spanish notification), then the last remaining passkey must be refused.
        var passkeys = await ListPasskeysAsync(client, ct);
        var notebookId = passkeys.Single(passkey => passkey.GetProperty("name").GetString() == "Notebook")
            .GetProperty("id").GetString()!;
        using (var removed = await client.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(notebookId), null, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        var removedEmail = await WaitForSubjectAsync(email, EsPasskeyRemovedSubject, "Notebook", ct);
        Assert.Contains("Notebook", removedEmail.Text, StringComparison.Ordinal);

        var lastId = (await ListPasskeysAsync(client, ct)).Single().GetProperty("id").GetString()!;
        using (var refused = await client.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(lastId), null, ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("identity.lastPasskey", problem.GetProperty("code").GetString());
        }

        Assert.Single(await ListPasskeysAsync(client, ct));

        // Recovery is requested anonymously; the Spanish recovery email carries the link.
        using var anonymous = new IdentityClient(_api);
        using (var recovery = await IdentityFlow.RequestRecoveryAsync(anonymous, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, recovery.StatusCode);
        }

        var recoveryEmail = await WaitForSubjectAsync(email, EsRecoverySubject, null, ct);
        var link = IdentityFlow.ParseRecoveryLink(recoveryEmail.Text);

        using var recoveredPasskey = new SoftwareAuthenticator();
        using (var completed = await CompleteRecoveryAsync(anonymous, link, recoveredPasskey, "Novo celular", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        }

        // The session that existed before recovery is over, and the recovered passkey signs in.
        using (var oldSession = await client.Http.GetAsync(MeUri, ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        }

        using (var signInWithRecovered = await IdentityFlow.SignInAsync(anonymous, recoveredPasskey, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signInWithRecovered.StatusCode);
        }

        using (var newSession = await anonymous.Http.GetAsync(MeUri, ct))
        {
            Assert.Equal(HttpStatusCode.OK, newSession.StatusCode);
        }

        // Passwordless by design: no account stores a password hash.
        await using var context = CreateContext();
        Assert.Equal(0, await context.Users.CountAsync(user => user.PasswordHash != null, ct));
    }

    public async ValueTask InitializeAsync()
    {
        // Touching Services starts the Worker host, which sends the queued emails.
        _ = _worker.Services;
        await ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseAspireBootNpgsql(infrastructure.PostgresConnectionString).UseStandaloneIdentitySchema();
        return new AppDbContext(options.Options);
    }

    private static async Task<ProfileBody> GetProfileAsync(IdentityClient client, CancellationToken ct)
    {
        using var response = await client.Http.GetAsync(MeUri, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ProfileBody>(ct)
            ?? throw new InvalidOperationException("The profile response was empty.");
    }

    private static async Task<JsonElement[]> ListPasskeysAsync(IdentityClient client, CancellationToken ct)
    {
        using var response = await client.Http.GetAsync(PasskeysUri, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement[]>(ct) ?? [];
    }

    /// <summary>Runs the add-passkey ceremony for the signed-in user with a new software credential.</summary>
    private static async Task<HttpResponseMessage> AddPasskeyAsync(
        IdentityClient client, SoftwareAuthenticator authenticator, string name, CancellationToken ct)
    {
        await client.RefreshAntiforgeryTokenAsync(ct);
        using var options = await client.SendUnsafeAsync(HttpMethod.Post, AddPasskeyOptionsUri, null, ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var creationOptions = await options.Content.ReadAsStringAsync(ct);

        return await client.SendUnsafeAsync(HttpMethod.Post, PasskeysUri, JsonContent.Create(new
        {
            credential = JsonDocument.Parse(authenticator.CreateAttestation(creationOptions)).RootElement,
            name,
        }), ct);
    }

    /// <summary>Runs the recovery ceremony with the link from the email and registers the replacement passkey.</summary>
    private static async Task<HttpResponseMessage> CompleteRecoveryAsync(
        IdentityClient client, (string UserId, string Token) link, SoftwareAuthenticator authenticator, string name, CancellationToken ct)
    {
        using var options = await client.SendUnsafeAsync(HttpMethod.Post, RegistrationOptionsUri, JsonContent.Create(new
        {
            userId = link.UserId,
            token = link.Token,
            purpose = "recover",
        }), ct);
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
    /// Polls Mailpit until a message to the recipient has the given subject (the subject carries the language)
    /// and, when given, the fragment in its text. The inbox is shared by every email of the journey.
    /// </summary>
    private async Task<MailpitMessage> WaitForSubjectAsync(string recipient, string subject, string? textFragment, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(MailTimeout);

        try
        {
            while (true)
            {
                foreach (var summary in await infrastructure.Mailpit.SearchAsync(recipient, timeout.Token))
                {
                    var message = await infrastructure.Mailpit.GetMessageAsync(summary.Id, timeout.Token);
                    if (message.Subject == subject && (textFragment is null || message.Text.Contains(textFragment, StringComparison.Ordinal)))
                    {
                        return message;
                    }
                }

                await Task.Delay(PollInterval, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No email '{subject}' to '{recipient}' arrived in Mailpit within {MailTimeout}.");
        }
    }

    private sealed record ProfileBody(Guid Id, string DisplayName, string Email, string Culture);

    /// <summary>
    /// Validates the security stamp on every request, so a stamp change ends a session at once. The journey needs it
    /// to prove that the session from before the recovery is over. Passkey removal refreshes the current sign-in, so it
    /// keeps the session alive.
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
}