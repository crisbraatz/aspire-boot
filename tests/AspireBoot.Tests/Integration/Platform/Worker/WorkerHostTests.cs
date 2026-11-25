using System.Net;

using AspireBoot.Tests.Integration.Infrastructure;

namespace AspireBoot.Tests.Integration.Platform.Worker;

[Trait("Category", "Integration")]
public sealed class WorkerHostTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootWorkerFactory _factory = new(infrastructure);

    [Fact]
    public async Task WorkerHost_StartsInMemoryAndReportsLive()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}