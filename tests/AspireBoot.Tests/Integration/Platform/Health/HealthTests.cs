using System.Net;

using AspireBoot.Tests.Integration.Infrastructure;

namespace AspireBoot.Tests.Integration.Platform.Health;

[Trait("Category", "Integration")]
public sealed class HealthTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootApiFactory _factory = new(infrastructure);

    [Fact]
    public async Task Health_WithRealInfrastructure_IsHealthy()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task UnknownApiEndpoint_Anonymous_RequiresAuthentication()
    {
        // The fallback authorization policy protects anything not explicitly marked anonymous.
        using var client = _factory.CreateHttpsClient();

        var response = await client.GetAsync(new Uri("/api/platform/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}