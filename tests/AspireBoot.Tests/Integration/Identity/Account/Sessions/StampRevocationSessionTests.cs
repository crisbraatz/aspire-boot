using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Identity.Admin;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Npgsql;

namespace AspireBoot.Tests.Integration.Identity.Account.Sessions;

/// <summary>Every stamp change also revokes the account's session rows in the same transaction (ADR 0007).</summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class StampRevocationSessionTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri MeUri = new("/api/identity/account/me", UriKind.Relative);

    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    public ValueTask InitializeAsync()
    {
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _worker.DisposeAsync();

    [Fact]
    public async Task Recovery_RevokesEarlierRows_KeepsTheNewOne_AndAuditsTheRevocation()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = new AspireBootApiFactory(infrastructure);
        const string email = "stamp-recovery@aspireboot.test";
        using var owner = new IdentityClient(api);
        using var passkey = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, owner, passkey, email, ct);
        var firstSession = (await SessionsOfAsync(api, userId, ct)).Single().Id;
        await infrastructure.Mailpit.DeleteAllAsync(ct);

        using var recovering = new IdentityClient(api);
        using (var request = await IdentityFlow.RequestRecoveryAsync(recovering, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        }

        var link = IdentityFlow.ParseRecoveryLink((await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct)).Text);
        using var recovered = new SoftwareAuthenticator();
        using var metrics = new SessionsEndedListener();
        using (var complete = await ImmediateRevocationTests.CompleteRecoveryAsync(recovering, link, recovered, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        }

        Assert.True(metrics.Has("stamp", 1));
        var rows = await SessionsOfAsync(api, userId, ct);
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows.Single(row => row.Id == firstSession).RevokedAt);
        Assert.Null(rows.Single(row => row.Id != firstSession).RevokedAt);
        await ImmediateRevocationTests.AssertStatusAsync(owner, HttpStatusCode.Unauthorized, ct);
        await ImmediateRevocationTests.AssertStatusAsync(recovering, HttpStatusCode.OK, ct);
        Assert.True(await AuditCountAsync(firstSession, ct) >= 2);
    }

    [Fact]
    public async Task RoleChange_RevokesAllTheTargetsRows_AndAuditsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId, key) = await AdminSession.SignInKeepingAuthenticatorAsync(api, infrastructure, "stamp-role@aspireboot.test", ct);
        using var _member = member;
        using var _key = key;
        using var second = new IdentityClient(api);
        using (var signIn = await IdentityFlow.SignInAsync(second, key, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        Assert.Equal(2, (await SessionsOfAsync(api, memberId, ct)).Count(row => row.RevokedAt is null));

        using var metrics = new SessionsEndedListener();
        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, new Uri($"/api/identity/admin/users/{memberId}/role", UriKind.Relative), JsonContent.Create(new { role = "Admin" }), ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(metrics.Has("stamp", 2));

        var rows = await SessionsOfAsync(api, memberId, ct);
        Assert.All(rows, row => Assert.NotNull(row.RevokedAt));
        await ImmediateRevocationTests.AssertStatusAsync(member, HttpStatusCode.Unauthorized, ct);
        await ImmediateRevocationTests.AssertStatusAsync(second, HttpStatusCode.Unauthorized, ct);
        Assert.True(await AuditCountAsync(rows[0].Id, ct) >= 2);
    }

    [Fact]
    public async Task PasskeyRemoval_RevokesOtherRows_AndTheCurrentSessionKeepsItsSid()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = new AspireBootApiFactory(infrastructure);
        using var acting = new IdentityClient(api);
        using var first = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, acting, first, "stamp-passkey@aspireboot.test", ct);
        await acting.RefreshAntiforgeryTokenAsync(ct);
        var currentSid = (await SessionsOfAsync(api, userId, ct)).Single().Id;

        using var other = new IdentityClient(api);
        using (var signIn = await IdentityFlow.SignInAsync(other, first, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        using var second = new SoftwareAuthenticator();
        using (var added = await ImmediateRevocationTests.AddPasskeyAsync(acting, second, "Notebook", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        var notebookId = await ImmediateRevocationTests.PasskeyIdAsync(acting, "Notebook", ct);
        using var metrics = new SessionsEndedListener();
        using (var removed = await acting.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(notebookId), null, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        Assert.True(metrics.Has("stamp", 1));
        var rows = await SessionsOfAsync(api, userId, ct);
        Assert.Equal(2, rows.Count);
        Assert.Null(rows.Single(row => row.Id == currentSid).RevokedAt);
        Assert.NotNull(rows.Single(row => row.Id != currentSid).RevokedAt);
        await ImmediateRevocationTests.AssertStatusAsync(other, HttpStatusCode.Unauthorized, ct);
        await ImmediateRevocationTests.AssertStatusAsync(acting, HttpStatusCode.OK, ct);
        Assert.Equal(2, (await SessionsOfAsync(api, userId, ct)).Count);
    }

    [Fact]
    public async Task CookieIssueFailure_KeepsTheConfirmationCommitted_LeavesAnOrphanRow_AndReturnsProblemDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = new CookieFailureApiFactory(infrastructure);
        const string email = "stamp-cookie-failure@aspireboot.test";
        using var client = new IdentityClient(api);
        using var passkey = new SoftwareAuthenticator();

        using var signUp = await IdentityFlow.SignUpAsync(client, email, ct);
        Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);
        var link = IdentityFlow.ParseConfirmationLink((await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct)).Text);

        using var options = await client.SendUnsafeAsync(
            HttpMethod.Post, new Uri("/api/identity/auth/registration/options", UriKind.Relative),
            JsonContent.Create(new { userId = link.UserId, token = link.Token, purpose = "confirm" }), ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var creationOptions = await options.Content.ReadAsStringAsync(ct);

        using var complete = await client.SendUnsafeAsync(HttpMethod.Post, new Uri("/api/identity/auth/registration", UriKind.Relative), JsonContent.Create(new
        {
            userId = link.UserId,
            token = link.Token,
            purpose = "confirm",
            credential = JsonDocument.Parse(passkey.CreateAttestation(creationOptions)).RootElement,
            name = "Meu celular",
        }), ct);

        Assert.Equal(HttpStatusCode.InternalServerError, complete.StatusCode);
        var body = await complete.Content.ReadAsStringAsync(ct);
        Assert.Equal("application/problem+json", complete.Content.Headers.ContentType?.MediaType);
        Assert.Contains("identity.signInRequired", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InjectedCookieFailure", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        var userId = Guid.Parse(link.UserId);
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.FindByIdAsync(userId.ToString()))!.EmailConfirmed);
        }

        var rows = await SessionsOfAsync(api, userId, ct);
        var orphan = Assert.Single(rows);
        Assert.Null(orphan.RevokedAt);
        Assert.True(orphan.ExpiresAt > DateTimeOffset.UtcNow);
        await ImmediateRevocationTests.AssertStatusAsync(client, HttpStatusCode.Unauthorized, ct);
    }

    private async Task ResetAsync(CancellationToken ct)
    {
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
    }

    private static async Task<List<UserSession>> SessionsOfAsync(AspireBootApiFactory api, Guid userId, CancellationToken ct)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions.AsNoTracking().Where(session => session.UserId == userId).OrderBy(session => session.CreatedAt).ToListAsync(ct);
    }

    private async Task<long> AuditCountAsync(Guid sessionId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            $"select count(*) from audit_entries where entity_type = 'UserSession' and entity_key = '{sessionId}'", connection);
        return (long)(await command.ExecuteScalarAsync(ct) ?? 0L);
    }

    /// <summary>Swaps SessionSignIn for a subclass whose cookie issuing throws: the only seam is the virtual IssueCookieAsync.</summary>
    private sealed class CookieFailureApiFactory(InfrastructureFixture infrastructure) : AspireBootApiFactory(infrastructure)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<SessionSignIn>();
                services.AddScoped<SessionSignIn, FailingSessionSignIn>();
            });
        }
    }

    private sealed class FailingSessionSignIn(
        SignInManager<User> signIn,
        AppDbContext db,
        TimeProvider clock,
        IOptionsMonitor<CookieAuthenticationOptions> cookieOptions) : SessionSignIn(signIn, db, clock, cookieOptions)
    {
        public override Task IssueCookieAsync(User user, bool isPersistent, Guid sessionId) =>
            throw new InvalidOperationException("InjectedCookieFailure");
    }
}