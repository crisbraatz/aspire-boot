using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Api.Shared;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Tests.Integration.Identity.Account.Sessions;

/// <summary>The sessions endpoints: list, end one, end the others (ADR 0007).</summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class SessionEndpointsTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const string ChromeOnMac = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private const string PeerIp = "203.0.113.77";
    private static readonly Uri SessionsUri = new("/api/identity/account/sessions", UriKind.Relative);
    private static readonly Uri EndOthersUri = new("/api/identity/account/sessions/end-others", UriKind.Relative);

    private readonly CapturingLogs _logs = new();
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);
    private LoggingApiFactory? _apiInstance;

    private LoggingApiFactory _api => _apiInstance ??= new LoggingApiFactory(infrastructure, _logs);

    public ValueTask InitializeAsync()
    {
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        if (_apiInstance is not null)
        {
            await _apiInstance.DisposeAsync();
        }
    }

    [Fact]
    public async Task List_ShowsTheMaskedIpBrowserAndOs_MarksTheCurrentSession_AndIsNotCached()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var first = NewClient(_api);
        using var passkey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, first, passkey, "list@aspireboot.test", ct);
        using var second = NewClient(_api);
        using (var signIn = await IdentityFlow.SignInAsync(second, passkey, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        using var response = await second.Http.GetAsync(SessionsUri, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement[]>(ct))!;
        Assert.Equal(2, items.Length);
        Assert.Single(items, item => item.GetProperty("current").GetBoolean());
        Assert.All(items, item =>
        {
            Assert.Equal("203.0.*.*", item.GetProperty("ipMasked").GetString());
            Assert.Equal("chrome", item.GetProperty("browser").GetString());
            Assert.Equal("macos", item.GetProperty("os").GetString());
        });
    }

    [Fact]
    public async Task EndingAnotherSession_Returns204_AndItsNextRequestIs401()
    {
        var ct = TestContext.Current.CancellationToken;
        var (acting, other, _) = await TwoSessionsAsync(_api, "end-one@aspireboot.test", ct);
        using var _a = acting;
        using var _o = other;
        var otherId = await NonCurrentIdAsync(acting, ct);
        await ImmediateRevocationTests.AssertStatusAsync(other, HttpStatusCode.OK, ct);

        using var ended = await acting.SendUnsafeAsync(HttpMethod.Delete, SessionUri(otherId), null, ct);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        await ImmediateRevocationTests.AssertStatusAsync(other, HttpStatusCode.Unauthorized, ct);
        await ImmediateRevocationTests.AssertStatusAsync(acting, HttpStatusCode.OK, ct);
    }

    [Fact]
    public async Task EndOthers_KeepsTheCurrentSession_AndReturnsTheCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var (acting, other, passkey) = await TwoSessionsAsync(_api, "end-others@aspireboot.test", ct);
        using var _a = acting;
        using var _o = other;
        using var _p = passkey;
        using var third = NewClient(_api);
        using (var signIn = await IdentityFlow.SignInAsync(third, passkey, keepSignedIn: false, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        }

        using var response = await acting.SendUnsafeAsync(HttpMethod.Post, EndOthersUri, null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("ended").GetInt32());
        await ImmediateRevocationTests.AssertStatusAsync(other, HttpStatusCode.Unauthorized, ct);
        await ImmediateRevocationTests.AssertStatusAsync(third, HttpStatusCode.Unauthorized, ct);
        await ImmediateRevocationTests.AssertStatusAsync(acting, HttpStatusCode.OK, ct);
        using var list = await acting.Http.GetAsync(SessionsUri, ct);
        Assert.Single((await list.Content.ReadFromJsonAsync<JsonElement[]>(ct))!);

        using var again = await acting.SendUnsafeAsync(HttpMethod.Post, EndOthersUri, null, ct);
        Assert.Equal(0, (await again.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("ended").GetInt32());
    }

    [Fact]
    public async Task AnotherUsersOrUnknownSession_Returns404_AndLeavesTheOwnerUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var attacker = NewClient(_api);
        using var attackerKey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, attacker, attackerKey, "idor-a@aspireboot.test", ct);
        await attacker.RefreshAntiforgeryTokenAsync(ct);
        using var victim = NewClient(_api);
        using var victimKey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, victim, victimKey, "idor-b@aspireboot.test", ct);
        var victimSession = await CurrentIdAsync(victim, ct);

        using var foreign = await attacker.SendUnsafeAsync(HttpMethod.Delete, SessionUri(victimSession), null, ct);
        using var unknown = await attacker.SendUnsafeAsync(HttpMethod.Delete, SessionUri(Guid.NewGuid()), null, ct);

        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "identity.sessionNotFound", ct);
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "identity.sessionNotFound", ct);
        await ImmediateRevocationTests.AssertStatusAsync(victim, HttpStatusCode.OK, ct);
    }

    [Fact]
    public async Task EndingAnAlreadyEndedSession_Returns204Again()
    {
        var ct = TestContext.Current.CancellationToken;
        var (acting, other, _) = await TwoSessionsAsync(_api, "idempotent@aspireboot.test", ct);
        using var _a = acting;
        using var _o = other;
        var otherId = await NonCurrentIdAsync(acting, ct);

        using var first = await acting.SendUnsafeAsync(HttpMethod.Delete, SessionUri(otherId), null, ct);
        using var second = await acting.SendUnsafeAsync(HttpMethod.Delete, SessionUri(otherId), null, ct);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task EndingTheCurrentSession_Returns409_AndKeepsItActive()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = NewClient(_api);
        using var passkey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, passkey, "current@aspireboot.test", ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        var current = await CurrentIdAsync(client, ct);

        using var response = await client.SendUnsafeAsync(HttpMethod.Delete, SessionUri(current), null, ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "identity.currentSession", ct);
        await ImmediateRevocationTests.AssertStatusAsync(client, HttpStatusCode.OK, ct);
    }

    [Fact]
    public async Task UnsafeSessionRequests_WithoutTheAntiforgeryToken_AreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = NewClient(_api);
        using var passkey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, passkey, "xsrf-sessions@aspireboot.test", ct);

        using var delete = await client.Http.DeleteAsync(SessionUri(Guid.NewGuid()), ct);
        using var endOthers = await client.Http.PostAsync(EndOthersUri, null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Contains("antiforgery.invalid", await delete.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, endOthers.StatusCode);
        Assert.Contains("antiforgery.invalid", await endOthers.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var http = _api.CreateHttpsClient();

        using var response = await http.GetAsync(SessionsUri, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void EverySessionsEndpoint_DeclaresTheAuthenticatedRateLimitPolicy()
    {
        var endpoints = _api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/account/sessions", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, endpoints.Count);
        Assert.All(endpoints, endpoint =>
            Assert.Equal(RateLimitPolicies.Authenticated, endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
    }

    [Fact]
    public async Task Logs_NeverContainTheFullIpOrTheUserAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (acting, other, _) = await TwoSessionsAsync(_api, "logs-sessions@aspireboot.test", ct);
        using var _a = acting;
        using var _o = other;
        var otherId = await NonCurrentIdAsync(acting, ct);
        using (await acting.SendUnsafeAsync(HttpMethod.Delete, SessionUri(otherId), null, ct))
        {
        }

        using (await acting.SendUnsafeAsync(HttpMethod.Post, EndOthersUri, null, ct))
        {
        }

        Assert.Contains(_logs.Messages, message => message.Contains(otherId.ToString("D"), StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Messages, message => message.Contains(PeerIp, StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Messages, message => message.Contains("Mozilla", StringComparison.Ordinal) || message.Contains("Chrome/120", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PeerInsideTheKnownNetwork_UsesTheForwardedAddress()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = new AspireBootApiFactory(
            infrastructure,
            new Dictionary<string, string?> { ["ReverseProxy:KnownNetworks:0"] = "10.0.0.0/8" },
            IPAddress.Parse("10.1.2.3"));

        Assert.Equal("198.51.*.*", await RegisteredIpAsync(api, "198.51.100.9", "ip-inside@aspireboot.test", ct));
    }

    [Fact]
    public async Task PeerOutsideTheKnownNetwork_IgnoresTheForwardedHeader()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = new AspireBootApiFactory(
            infrastructure,
            new Dictionary<string, string?> { ["ReverseProxy:KnownNetworks:0"] = "10.0.0.0/8" },
            IPAddress.Parse("192.0.2.5"));

        Assert.Equal("192.0.*.*", await RegisteredIpAsync(api, "198.51.100.9", "ip-outside@aspireboot.test", ct));
    }

    private async Task<string?> RegisteredIpAsync(AspireBootApiFactory api, string forwardedFor, string email, CancellationToken ct)
    {
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(api);
        client.Http.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        using var passkey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, passkey, email, ct);

        using var response = await client.Http.GetAsync(SessionsUri, ct);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement[]>(ct))!;
        return items.Single().GetProperty("ipMasked").GetString();
    }

    private async Task<(IdentityClient Acting, IdentityClient Other, SoftwareAuthenticator Passkey)> TwoSessionsAsync(
        AspireBootApiFactory api, string email, CancellationToken ct)
    {
        await infrastructure.ResetDatabaseAsync();
        var acting = NewClient(api);
        var passkey = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, acting, passkey, email, ct);
        await acting.RefreshAntiforgeryTokenAsync(ct);
        var other = NewClient(api);
        using var signIn = await IdentityFlow.SignInAsync(other, passkey, keepSignedIn: false, ct);
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        return (acting, other, passkey);
    }

    private static IdentityClient NewClient(AspireBootApiFactory api)
    {
        var client = new IdentityClient(api);
        client.Http.DefaultRequestHeaders.UserAgent.ParseAdd(ChromeOnMac);
        return client;
    }

    private static Uri SessionUri(Guid id) => new($"/api/identity/account/sessions/{id:D}", UriKind.Relative);

    private static async Task<Guid> NonCurrentIdAsync(IdentityClient client, CancellationToken ct) =>
        await IdAsync(client, current: false, ct);

    private static async Task<Guid> CurrentIdAsync(IdentityClient client, CancellationToken ct) =>
        await IdAsync(client, current: true, ct);

    private static async Task<Guid> IdAsync(IdentityClient client, bool current, CancellationToken ct)
    {
        using var response = await client.Http.GetAsync(SessionsUri, ct);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement[]>(ct))!;
        return items.First(item => item.GetProperty("current").GetBoolean() == current).GetProperty("id").GetGuid();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken ct)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private sealed class LoggingApiFactory(InfrastructureFixture infrastructure, CapturingLogs logs)
        : AspireBootApiFactory(infrastructure, null, IPAddress.Parse(PeerIp))
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(logs));
        }
    }

    private sealed class CapturingLogs : ILoggerProvider, ILogger
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