using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Account.Passkeys;

[Trait("Category", "Integration")]
public sealed class PasskeyManagementTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri PasskeysUri = new("/api/identity/account/passkeys", UriKind.Relative);
    private static readonly Uri BeginAddUri = new("/api/identity/account/passkeys/options", UriKind.Relative);
    private static readonly Regex PasskeyIdPattern = new(@"""id"":""(?<id>[A-Za-z0-9_-]+)""", RegexOptions.CultureInvariant);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task ListPasskeys_ReturnsTheRegisteredPasskeyWithNameAndCreatedDate()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "pk-list@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var response = await client.Http.GetAsync(PasskeysUri, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var passkeys = await response.Content.ReadFromJsonAsync<JsonElement[]>(ct);
        Assert.NotNull(passkeys);
        var passkey = Assert.Single(passkeys);
        Assert.Equal("Meu celular", passkey.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(passkey.GetProperty("id").GetString()));
        Assert.NotEqual(default, passkey.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task AddPasskey_SavesASecondPasskey_AndSendsAnEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        const string email = "pk-add@aspireboot.test";
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, email, ct);
        await client.RefreshAntiforgeryTokenAsync(ct);

        using var added = await AddPasskeyAsync(client, authenticator, "Notebook", ct);

        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        var passkeys = await ListAsync(client, ct);
        Assert.Equal(2, passkeys.Length);
        Assert.Contains(passkeys, passkey => passkey.GetProperty("name").GetString() == "Notebook");

        var message = await WaitForEmailAsync(email, "Notebook", "foi adicionada", ct);
        Assert.Contains("Notebook", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamePasskey_UpdatesTheName()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "pk-rename@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        var id = (await ListAsync(client, ct)).Single().GetProperty("id").GetString()!;

        using var renamed = await client.SendUnsafeAsync(HttpMethod.Patch, IdentityFlow.PasskeyUri(id), JsonContent.Create(new { name = "Trabalho" }), ct);

        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        var passkeys = await ListAsync(client, ct);
        Assert.Equal("Trabalho", passkeys.Single().GetProperty("name").GetString());
    }

    [Fact]
    public async Task RenamePasskey_RejectsANameLongerThanFiftyCharacters()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "pk-long@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        var id = (await ListAsync(client, ct)).Single().GetProperty("id").GetString()!;

        using var response = await client.SendUnsafeAsync(HttpMethod.Patch, IdentityFlow.PasskeyUri(id), JsonContent.Create(new { name = new string('x', 51) }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemovePasskey_RemovesASecondPasskey_AndSendsAnEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        const string email = "pk-remove@aspireboot.test";
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, email, ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        using (var added = await AddPasskeyAsync(client, authenticator, "Notebook", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        var notebookId = (await ListAsync(client, ct))
            .Single(passkey => passkey.GetProperty("name").GetString() == "Notebook")
            .GetProperty("id").GetString()!;
        using var removed = await client.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(notebookId), null, ct);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Single(await ListAsync(client, ct));

        var message = await WaitForEmailAsync(email, "Notebook", "foi removida", ct);
        Assert.Contains("Notebook", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovePasskey_LastPasskey_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "pk-last@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        var id = (await ListAsync(client, ct)).Single().GetProperty("id").GetString()!;

        using var response = await client.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(id), null, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("identity.lastPasskey", problem.GetProperty("code").GetString());
        Assert.Single(await ListAsync(client, ct));
    }

    [Fact]
    public async Task PasskeyOfAnotherUser_Returns404_ForRenameAndRemove()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var owner = new IdentityClient(_api);
        using var ownerAuthenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, owner, ownerAuthenticator, "pk-owner@aspireboot.test", ct);
        await owner.RefreshAntiforgeryTokenAsync(ct);
        var ownerPasskeyId = (await ListAsync(owner, ct)).Single().GetProperty("id").GetString()!;

        using var other = new IdentityClient(_api);
        using var otherAuthenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, other, otherAuthenticator, "pk-other@aspireboot.test", ct);
        await other.RefreshAntiforgeryTokenAsync(ct);

        using var renamed = await other.SendUnsafeAsync(HttpMethod.Patch, IdentityFlow.PasskeyUri(ownerPasskeyId), JsonContent.Create(new { name = "Roubado" }), ct);
        using var removed = await other.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(ownerPasskeyId), null, ct);

        Assert.Equal(HttpStatusCode.NotFound, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal("Meu celular", (await ListAsync(owner, ct)).Single().GetProperty("name").GetString());
    }

    [Fact]
    public async Task ConcurrentRemovals_DeleteOnlyOneOfTwoPasskeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "pk-race@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        using (var added = await AddPasskeyAsync(client, authenticator, "Notebook", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        var ids = (await ListAsync(client, ct)).Select(passkey => passkey.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(2, ids.Length);

        var first = client.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(ids[0]), null, ct);
        var second = client.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(ids[1]), null, ct);
        var responses = await Task.WhenAll(first, second);

        var statuses = responses.Select(response => response.StatusCode).OrderBy(status => (int)status).ToArray();
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], statuses);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Single(await ListAsync(client, ct));
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

    private static async Task<JsonElement[]> ListAsync(IdentityClient client, CancellationToken ct)
    {
        using var response = await client.Http.GetAsync(PasskeysUri, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement[]>(ct) ?? [];
    }

    /// <summary>Runs the add-passkey ceremony for the signed-in user with a new software credential.</summary>
    private static async Task<HttpResponseMessage> AddPasskeyAsync(
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

    /// <summary>Polls Mailpit until a message to the recipient has the passkey name in its body and the event in its subject (pt-BR copy, the test account culture).</summary>
    private async Task<MailpitMessage> WaitForEmailAsync(string recipient, string passkeyName, string eventInSubject, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(MailTimeout);

        while (true)
        {
            foreach (var summary in await infrastructure.Mailpit.SearchAsync(recipient, timeout.Token))
            {
                var message = await infrastructure.Mailpit.GetMessageAsync(summary.Id, timeout.Token);
                if (message.Subject.Contains(eventInSubject, StringComparison.Ordinal) && message.Text.Contains(passkeyName, StringComparison.Ordinal))
                {
                    return message;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
        }
    }
}