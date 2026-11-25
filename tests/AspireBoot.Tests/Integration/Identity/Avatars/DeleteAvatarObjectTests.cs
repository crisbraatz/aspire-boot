using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

using Wolverine;

namespace AspireBoot.Tests.Integration.Identity.Avatars;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class DeleteAvatarObjectTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootWorkerFactory _factory = new(infrastructure);

    [Fact]
    public async Task SupersededObject_IsDeletedFromStorage()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var key = $"avatars/{userId:N}/{Guid.NewGuid():N}.webp";

        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAvatarStorage>();
        await storage.PutAsync(key, new byte[] { 1, 2, 3 }, ct);

        await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(new AvatarObjectSuperseded(userId, key), ct);

        Assert.Null(await storage.OpenReadAsync(key, ct));
    }

    [Fact]
    public async Task SupersededObject_RedeliveredAfterDeletion_IsHarmless()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var key = $"avatars/{userId:N}/{Guid.NewGuid():N}.webp";
        var message = new AvatarObjectSuperseded(userId, key);

        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var storage = scope.ServiceProvider.GetRequiredService<IAvatarStorage>();
        await storage.PutAsync(key, new byte[] { 1, 2, 3 }, ct);

        await bus.InvokeAsync(message, ct);
        await bus.InvokeAsync(message, ct);

        Assert.Null(await storage.OpenReadAsync(key, ct));
    }

    public ValueTask InitializeAsync()
    {
        // Starting the Worker host provisions the avatar bucket.
        _ = _factory.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}