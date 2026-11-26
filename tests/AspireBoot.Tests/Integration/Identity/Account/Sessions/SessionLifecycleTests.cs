using System.Net;
using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AspireBoot.Tests.Integration.Identity.Account.Sessions;

/// <summary>Every sign-in path creates a session row, the cookie carries its id, and sign-out and the heartbeat keep it consistent (ADR 0007).</summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class SessionLifecycleTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string ChromeOnMac = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private const string ClientIp = "203.0.113.77";
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly CapturingLoggerProvider _logs = new();
    private TestApiFactory? _apiInstance;
    private readonly AspireBootApiFactory _instanceB = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    private TestApiFactory _api => _apiInstance ??= new TestApiFactory(infrastructure, _clock, _logs);

    [Fact]
    public async Task EverySignInPath_CreatesOneRow_AndTheCookieAuthenticates()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string email = "paths@aspireboot.test";
        using var owner = new IdentityClient(_api);
        using var passkey = new SoftwareAuthenticator();

        var userId = await IdentityFlow.RegisterAsync(infrastructure, owner, passkey, email, ct);
        Assert.Single(await SessionsOfAsync(userId, ct));
        await ImmediateRevocationTests.AssertStatusAsync(owner, HttpStatusCode.OK, ct);

        using var signingIn = new IdentityClient(_api);
        using (var signIn = await IdentityFlow.SignInAsync(signingIn, passkey, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        Assert.Equal(2, (await SessionsOfAsync(userId, ct)).Count);
        await ImmediateRevocationTests.AssertStatusAsync(signingIn, HttpStatusCode.OK, ct);

        await infrastructure.Mailpit.DeleteAllAsync(ct);
        using var recovering = new IdentityClient(_api);
        using (var request = await IdentityFlow.RequestRecoveryAsync(recovering, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        }

        var link = IdentityFlow.ParseRecoveryLink((await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct)).Text);
        using var recovered = new SoftwareAuthenticator();
        using (var complete = await ImmediateRevocationTests.CompleteRecoveryAsync(recovering, link, recovered, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        }

        Assert.Equal(3, (await SessionsOfAsync(userId, ct)).Count);
        await ImmediateRevocationTests.AssertStatusAsync(recovering, HttpStatusCode.OK, ct);
    }

    [Fact]
    public async Task CookieWithoutSid_IsRejected_AndWithSid_IsAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, sessionId) = await RegisterAsync("legacy@aspireboot.test", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, await IdentityFlow.StatusWithCookieAsync(_api, await ForgeCookieAsync(userId, null), ct));
        Assert.Equal(HttpStatusCode.OK, await IdentityFlow.StatusWithCookieAsync(_api, await ForgeCookieAsync(userId, sessionId), ct));
    }

    [Fact]
    public async Task SignOut_RevokesTheRow_AndReplayingTheOldCookieIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var owner = new IdentityClient(_api);
        using var passkey = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, owner, passkey, "signout@aspireboot.test", ct);

        using var client = new IdentityClient(_api);
        string cookie;
        using (var signIn = await IdentityFlow.SignInAsync(client, passkey, keepSignedIn: false, ct))
        {
            cookie = IdentityFlow.SessionCookie(signIn)!;
        }

        Assert.Equal(HttpStatusCode.OK, await IdentityFlow.StatusWithCookieAsync(_api, cookie, ct));
        using var metrics = new SessionsEndedListener();
        Assert.Equal(HttpStatusCode.NoContent, await IdentityFlow.SignOutAsync(client, ct));
        Assert.True(metrics.Has("signout", 1));

        Assert.Equal(1, (await SessionsOfAsync(userId, ct)).Count(session => session.RevokedAt is not null));
        Assert.Equal(HttpStatusCode.Unauthorized, await IdentityFlow.StatusWithCookieAsync(_api, cookie, ct));
    }

    [Fact]
    public async Task SignOutOnInstanceA_RejectsTheCookieOnInstanceB_OnItsNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var owner = new IdentityClient(_api);
        using var passkey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, owner, passkey, "cross-session@aspireboot.test", ct);

        using var onA = new IdentityClient(_api);
        string cookie;
        using (var signIn = await IdentityFlow.SignInAsync(onA, passkey, keepSignedIn: false, ct))
        {
            cookie = IdentityFlow.SessionCookie(signIn)!;
        }

        // Warms the session entry from instance B.
        Assert.Equal(HttpStatusCode.OK, await IdentityFlow.StatusWithCookieAsync(_instanceB, cookie, ct));
        Assert.Equal(HttpStatusCode.OK, await IdentityFlow.StatusWithCookieAsync(_instanceB, cookie, ct));

        Assert.Equal(HttpStatusCode.NoContent, await IdentityFlow.SignOutAsync(onA, ct));

        Assert.Equal(HttpStatusCode.Unauthorized, await IdentityFlow.StatusWithCookieAsync(_instanceB, cookie, ct));
    }

    [Fact]
    public async Task Heartbeat_IsThrottled_WritesOncePerWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, sessionId) = await RegisterAsync("heartbeat@aspireboot.test", ct);
        var cookie = await ForgeCookieAsync(userId, sessionId);
        var created = (await SessionAsync(sessionId, ct)).LastSeenAt;

        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(HttpStatusCode.OK, await IdentityFlow.StatusWithCookieAsync(_api, cookie, ct));
        Assert.Equal(created, (await SessionAsync(sessionId, ct)).LastSeenAt);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.OK, await IdentityFlow.StatusWithCookieAsync(_api, cookie, ct));
        var touched = await SessionAsync(sessionId, ct);
        Assert.True(touched.LastSeenAt > created);

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, await IdentityFlow.StatusWithCookieAsync(_api, cookie, ct));
        Assert.Equal(touched.LastSeenAt, (await SessionAsync(sessionId, ct)).LastSeenAt);
    }

    [Fact]
    public async Task Heartbeat_NeverExtendsAnExpiredRow_AndOnlyMatchesTheOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, sessionId) = await RegisterAsync("expired@aspireboot.test", ct);
        var before = await SessionAsync(sessionId, ct);
        using var scope = _api.Services.CreateScope();
        var heartbeat = scope.ServiceProvider.GetRequiredService<IUserSessionHeartbeat>();

        Assert.False(await heartbeat.TouchAsync(sessionId, userId, before.ExpiresAt.AddMinutes(1), TimeSpan.FromMinutes(5), ct));
        Assert.False(await heartbeat.TouchAsync(sessionId, Guid.NewGuid(), before.LastSeenAt.AddMinutes(10), TimeSpan.FromMinutes(5), ct));
        Assert.True(await heartbeat.TouchAsync(sessionId, userId, before.LastSeenAt.AddMinutes(10), TimeSpan.FromMinutes(5), ct));
        Assert.False(await heartbeat.TouchAsync(sessionId, userId, before.LastSeenAt.AddMinutes(10), TimeSpan.FromMinutes(5), ct));

        var after = await SessionAsync(sessionId, ct);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt - TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task HeartbeatBetweenLoadAndSave_DoesNotBreakARevocation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, sessionId) = await RegisterAsync("race@aspireboot.test", ct);

        using var revokingScope = _api.Services.CreateScope();
        var revoking = revokingScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tracked = await revoking.UserSessions.SingleAsync(session => session.Id == sessionId, ct);

        using (var heartbeatScope = _api.Services.CreateScope())
        {
            var heartbeat = heartbeatScope.ServiceProvider.GetRequiredService<IUserSessionHeartbeat>();
            Assert.True(await heartbeat.TouchAsync(sessionId, userId, tracked.LastSeenAt.AddMinutes(10), TimeSpan.FromMinutes(5), ct));
        }

        tracked.Revoke(DateTimeOffset.UtcNow);
        await revoking.SaveChangesAsync(ct);

        Assert.NotNull((await SessionAsync(sessionId, ct)).RevokedAt);
    }

    [Fact]
    public async Task PersistentSignIn_KeepsAPersistentCookie_AndTheRefreshKeepsItsSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var owner = new IdentityClient(_api);
        using var first = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, owner, first, "persistent@aspireboot.test", ct);

        using var persistent = new IdentityClient(_api);
        using (var signIn = await IdentityFlow.SignInAsync(persistent, first, keepSignedIn: true, ct))
        {
            Assert.True(IdentityFlow.IsPersistentCookie(signIn));
        }

        await persistent.RefreshAntiforgeryTokenAsync(ct);
        using var second = new SoftwareAuthenticator();
        using (var added = await ImmediateRevocationTests.AddPasskeyAsync(persistent, second, "Notebook", ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        }

        var notebookId = await ImmediateRevocationTests.PasskeyIdAsync(persistent, "Notebook", ct);
        using (var removed = await persistent.SendUnsafeAsync(HttpMethod.Delete, IdentityFlow.PasskeyUri(notebookId), null, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
            Assert.True(IdentityFlow.IsPersistentCookie(removed));
        }

        await ImmediateRevocationTests.AssertStatusAsync(persistent, HttpStatusCode.OK, ct);
        Assert.Equal(2, (await SessionsOfAsync(userId, ct)).Count);
    }

    [Fact]
    public async Task SignIn_StoresOnlyMaskedValues_AndLogsNeitherTheIpNorTheUserAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var owner = new IdentityClient(_api);
        using var passkey = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, owner, passkey, "logs@aspireboot.test", ct);

        using var client = new IdentityClient(_api);
        client.Http.DefaultRequestHeaders.UserAgent.ParseAdd(ChromeOnMac);
        using (var signIn = await IdentityFlow.SignInAsync(client, passkey, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        var row = (await SessionsOfAsync(userId, ct)).OrderByDescending(session => session.CreatedAt).First();
        Assert.Equal("203.0.*.*", row.IpMasked);
        Assert.DoesNotContain("Mozilla", row.UserAgent, StringComparison.Ordinal);

        Assert.NotEmpty(_logs.Messages);
        Assert.DoesNotContain(_logs.Messages, message => message.Contains(ClientIp, StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("Mozilla", StringComparison.Ordinal) || message.Contains("Chrome/120", StringComparison.Ordinal));
    }

    public ValueTask InitializeAsync()
    {
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _instanceB.DisposeAsync();
        if (_apiInstance is not null)
        {
            await _apiInstance.DisposeAsync();
        }
    }

    private async Task<(Guid UserId, Guid SessionId)> RegisterAsync(string email, CancellationToken ct)
    {
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var passkey = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, passkey, email, ct);
        return (userId, (await SessionsOfAsync(userId, ct)).Single().Id);
    }

    private async Task<List<UserSession>> SessionsOfAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions.AsNoTracking().Where(session => session.UserId == userId).ToListAsync(ct);
    }

    private async Task<UserSession> SessionAsync(Guid sessionId, CancellationToken ct)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSessions.AsNoTracking().SingleAsync(session => session.Id == sessionId, ct);
    }

    /// <summary>Builds a valid application cookie for the account, with or without the session claim.</summary>
    private async Task<string> ForgeCookieAsync(Guid userId, Guid? sessionId)
    {
        using var scope = _api.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<User>>();
        var user = await users.FindByIdAsync(userId.ToString());
        var principal = await factory.CreateAsync(user!);
        if (sessionId is { } sid)
        {
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(SessionClaims.SessionId, sid.ToString("D")));
        }

        var cookie = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, IdentityConstants.ApplicationScheme);
        return cookie.TicketDataFormat.Protect(ticket);
    }

    private sealed class TestApiFactory(InfrastructureFixture infrastructure, FakeTimeProvider clock, CapturingLoggerProvider logs)
        : AspireBootApiFactory(infrastructure)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<ILoggerProvider>(logs);
                services.AddTransient<IStartupFilter, FixedRemoteIpFilter>();
            });
        }
    }

    // TestServer leaves the remote address empty, so a filter sets one before the rest of the pipeline.
    private sealed class FixedRemoteIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => [.. _messages];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _messages.Enqueue(formatter(state, exception) + " " + exception);

        public void Dispose()
        {
        }
    }
}