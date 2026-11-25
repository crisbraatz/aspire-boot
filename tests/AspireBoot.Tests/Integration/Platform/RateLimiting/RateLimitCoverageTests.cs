using System.Net;
using System.Text;
using System.Text.RegularExpressions;

using AspireBoot.Api.Shared;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Platform.RateLimiting;

/// <summary>
/// Architecture-style coverage over the real endpoint table: every /api endpoint carries a rate-limit policy, and every
/// unsafe /api endpoint rejects a request without the antiforgery header. A dedicated factory checks the 429 response.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed partial class RateLimitCoverageTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri SignUpUri = new("/api/identity/auth/signup", UriKind.Relative);
    private static readonly string[] UnsafeMethods = ["POST", "PUT", "PATCH", "DELETE"];

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public void EveryApiEndpoint_HasRateLimitPolicy()
    {
        var endpoints = ApiEndpoints(_api);

        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/api/platform/info");
        var unlimited = endpoints
            .Where(e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>() is null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        Assert.Empty(unlimited);
    }

    [Fact]
    public async Task EveryUnsafeApiEndpoint_RejectsRequestWithoutAntiforgeryToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();
        await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "coverage-xsrf@aspireboot.test", ct);

        var checkedRoutes = new List<string>();
        foreach (var endpoint in ApiEndpoints(_api))
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var method in methods.Where(m => UnsafeMethods.Contains(m, StringComparer.Ordinal)))
            {
                var uri = new Uri(RouteParameter().Replace(endpoint.RoutePattern.RawText!, "placeholder"), UriKind.Relative);
                // Endpoints that declare multipart input only match multipart requests, so send the shape they accept.
                using HttpContent content = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true
                    ? new MultipartFormDataContent { { new StringContent("x"), "other" } }
                    : new StringContent("{}", Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(new HttpMethod(method), uri) { Content = content };

                using var response = await client.Http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                Assert.True(
                    response.StatusCode == HttpStatusCode.BadRequest && body.Contains("antiforgery.invalid", StringComparison.Ordinal),
                    $"{method} {endpoint.RoutePattern.RawText} accepted a request without the antiforgery token ({(int)response.StatusCode}).");
                checkedRoutes.Add($"{method} {endpoint.RoutePattern.RawText}");
            }
        }

        Assert.Contains("POST /api/identity/auth/signup", checkedRoutes);
        Assert.Contains("PUT /api/identity/account/preferences", checkedRoutes);
        Assert.Contains("DELETE /api/identity/account/passkeys/{id}", checkedRoutes);
    }

    [Fact]
    public async Task ExceedingSignupLimit_Returns429_WithRetryAfter_AndProblemDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await using var limited = new SignupLimitedApiFactory(infrastructure);
        using var client = new IdentityClient(limited);

        using var first = await client.SendUnsafeAsync(HttpMethod.Post, SignUpUri, JsonContentOf("limit-first@aspireboot.test"), ct);
        using var second = await client.SendUnsafeAsync(HttpMethod.Post, SignUpUri, JsonContentOf("limit-second@aspireboot.test"), ct);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.TryGetValues("Retry-After", out var retryAfter), "A 429 must carry Retry-After.");
        Assert.True(int.Parse(retryAfter!.Single(), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Contains("rateLimited", await second.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
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

    private static StringContent JsonContentOf(string email) =>
        new($"{{\"displayName\":\"Ana\",\"email\":\"{email}\",\"culture\":\"pt-BR\"}}", Encoding.UTF8, "application/json");

    private static List<RouteEndpoint> ApiEndpoints(WebApplicationFactory<global::Program> factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .ToList();

    /// <summary>Dedicated factory: a signup limit of one request per window, so the second request is rejected.</summary>
    private sealed class SignupLimitedApiFactory(InfrastructureFixture infrastructure)
        : AspireBootApiFactory(infrastructure, new Dictionary<string, string?>
        {
            [$"RateLimiting:{RateLimitPolicies.Signup}:PermitLimit"] = "1",
        });
}