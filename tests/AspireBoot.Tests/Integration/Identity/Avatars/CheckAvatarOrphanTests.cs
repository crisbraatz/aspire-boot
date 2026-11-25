using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Wolverine;

namespace AspireBoot.Tests.Integration.Identity.Avatars;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class CheckAvatarOrphanTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly AspireBootWorkerFactory _factory = new(infrastructure);

    [Fact]
    public async Task OrphanCheck_DeletesKeyThatIsNotTheCurrentAvatar()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserWithTwoAvatarsAsync(ct);
        var superseded = AvatarReference.For(user.Id, Guid.NewGuid(), 1).Key;
        await PutObjectAsync(superseded, ct);

        await InvokeOrphanCheckAsync(user.Id, superseded, ct);

        Assert.False(await ExistsAsync(superseded, ct));
    }

    [Fact]
    public async Task OrphanCheck_KeepsTheCurrentAvatar()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserWithTwoAvatarsAsync(ct);
        var current = user.Avatar!.Key;
        await PutObjectAsync(current, ct);

        await InvokeOrphanCheckAsync(user.Id, current, ct);

        Assert.True(await ExistsAsync(current, ct));
    }

    [Fact]
    public async Task OrphanCheck_ForMissingUser_DeletesTheKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var key = AvatarReference.For(userId, Guid.NewGuid(), 1).Key;
        await PutObjectAsync(key, ct);

        await InvokeOrphanCheckAsync(userId, key, ct);

        Assert.False(await ExistsAsync(key, ct));
    }

    [Fact]
    public async Task OrphanCheck_RedeliveredAfterDeletion_IsHarmless()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserWithTwoAvatarsAsync(ct);
        var superseded = AvatarReference.For(user.Id, Guid.NewGuid(), 1).Key;
        await PutObjectAsync(superseded, ct);

        await InvokeOrphanCheckAsync(user.Id, superseded, ct);
        await InvokeOrphanCheckAsync(user.Id, superseded, ct);

        Assert.False(await ExistsAsync(superseded, ct));
        Assert.NotNull((await ReadUserAsync(user.Id, ct)).Avatar);
    }

    public async ValueTask InitializeAsync()
    {
        await infrastructure.ResetDatabaseAsync();

        // Starting the Worker host provisions the avatar bucket.
        _ = _factory.Services;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>Creates a user whose avatar has been replaced once, so the first key is superseded and the second is current.</summary>
    private async Task<User> CreateUserWithTwoAvatarsAsync(CancellationToken ct)
    {
        var user = User.Create("orphan@example.com", DisplayName.Create("Orphan Owner").Value, UiCulture.EnUs, new FakeTimeProvider(Now));
        await using (var context = CreateContext())
        {
            context.Users.Add(user);
            await context.SaveChangesAsync(ct);
        }

        await using (var context = CreateContext())
        {
            var stored = await context.Users.SingleAsync(u => u.Id == user.Id, ct);
            stored.ChangeAvatar(Guid.NewGuid());
            await context.SaveChangesAsync(ct);
        }

        await using (var context = CreateContext())
        {
            var stored = await context.Users.SingleAsync(u => u.Id == user.Id, ct);
            stored.ChangeAvatar(Guid.NewGuid());
            await context.SaveChangesAsync(ct);
        }

        return await ReadUserAsync(user.Id, ct);
    }

    private async Task<User> ReadUserAsync(Guid userId, CancellationToken ct)
    {
        await using var context = CreateContext();
        return await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
    }

    private async Task PutObjectAsync(string key, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAvatarStorage>().PutAsync(key, new byte[] { 1, 2, 3 }, ct);
    }

    private async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        await using var stream = await scope.ServiceProvider.GetRequiredService<IAvatarStorage>().OpenReadAsync(key, ct);
        return stream is not null;
    }

    private async Task InvokeOrphanCheckAsync(Guid userId, string key, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(new AvatarObjectOrphanCheck(userId, key), ct);
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseAspireBootNpgsql(infrastructure.PostgresConnectionString).UseStandaloneIdentitySchema();
        return new AppDbContext(options.Options);
    }
}