using System.Net;
using System.Text.Json;

using AspireBoot.Tests.Integration.Infrastructure;

namespace AspireBoot.Tests.Integration.Shared;

[Trait("Category", "Integration")]
public sealed class GetAntiforgeryTokenTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootApiFactory _factory = new(infrastructure);

    [Fact]
    public async Task GetToken_SetsReadableXsrfCookieWithSecureAttributes()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.GetAsync(new Uri("/api/antiforgery/token", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var xsrf = Assert.Single(cookies!, cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.Contains("secure", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.True(xsrf.Length > "XSRF-TOKEN=".Length + 1);
    }

    [Fact]
    public async Task GetToken_WhenRateLimitExceeded_Returns429ProblemWithRetryAfter()
    {
        await using var limited = new AspireBootApiFactory(infrastructure, new Dictionary<string, string?>
        {
            ["RateLimiting:authenticated:PermitLimit"] = "2",
        });
        using var client = limited.CreateHttpsClient();
        var uri = new Uri("/api/antiforgery/token", UriKind.Relative);

        await client.GetAsync(uri, TestContext.Current.CancellationToken);
        await client.GetAsync(uri, TestContext.Current.CancellationToken);
        var response = await client.GetAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("rateLimited", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("rateLimited", document.RootElement.GetProperty("code").GetString());
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}