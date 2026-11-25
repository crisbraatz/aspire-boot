using System.Net;
using System.Net.Http.Json;

using AspireBoot.Tests.Integration.Infrastructure;

namespace AspireBoot.Tests.Integration.Platform.GetAppInfo;

[Trait("Category", "Integration")]
public sealed class GetAppInfoTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootApiFactory _factory = new(infrastructure);

    [Fact]
    public async Task GetAppInfo_Anonymous_ReturnsPublicMetadata()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.GetAsync(new Uri("/api/platform/info", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AppInfo>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("AspireBoot", body.Name);
        Assert.Equal("Testing", body.Environment);
    }

    [Fact]
    public async Task PostToReadOnlyEndpoint_Anonymous_IsRejectedByFallbackPolicy()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.PostAsync(new Uri("/api/platform/info", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        // Only the GET endpoint is anonymous; any other method falls back to "authenticated user required".
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private sealed record AppInfo(string Name, string Version, string Environment);
}