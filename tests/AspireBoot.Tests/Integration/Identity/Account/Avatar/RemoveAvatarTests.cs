using System.Net;
using System.Net.Http.Headers;

using Amazon.S3;
using Amazon.S3.Model;

using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Identity.Account.Avatar;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class RemoveAvatarTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri AvatarUri = new("/api/identity/account/avatar", UriKind.Relative);
    private static readonly TimeSpan EventualTimeout = TimeSpan.FromSeconds(45);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);
    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public async Task RemovingTwice_Returns204Both_ClearsTheProfile_AndTheWorkerDeletesTheObject()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await SignedInAsync("avatar-remove@aspireboot.test", ct);
        await UploadAsync(client, ct);
        var key = Assert.Single(await ListKeysAsync(userId, ct));

        using var first = await client.SendUnsafeAsync(HttpMethod.Delete, AvatarUri, null, ct);
        using var second = await client.SendUnsafeAsync(HttpMethod.Delete, AvatarUri, null, ct);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Null(await CurrentKeyAsync(userId, ct));
        await EventuallyAsync(async () => !(await ListKeysAsync(userId, ct)).Contains(key), "the Worker to delete the removed object", ct);
    }

    [Fact]
    public async Task RemovingWhenThereIsNoAvatar_Returns204()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await SignedInAsync("avatar-remove-none@aspireboot.test", ct);

        using var response = await client.SendUnsafeAsync(HttpMethod.Delete, AvatarUri, null, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await CurrentKeyAsync(userId, ct));
    }

    [Fact]
    public async Task UploadingAfterARemoval_UsesANewVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await SignedInAsync("avatar-remove-reupload@aspireboot.test", ct);
        await UploadAsync(client, ct);
        using var removed = await client.SendUnsafeAsync(HttpMethod.Delete, AvatarUri, null, ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using var again = await UploadAsync(client, ct);

        Assert.Contains("/api/identity/account/avatar/2", await again.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new IdentityClient(_api);

        using var response = await client.SendUnsafeAsync(HttpMethod.Delete, AvatarUri, null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        await infrastructure.ResetDatabaseAsync();
        _ = _worker.Services;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private static async Task<HttpResponseMessage> UploadAsync(IdentityClient client, CancellationToken ct)
    {
        var file = new ByteArrayContent(AvatarTestImages.Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { file, "file", "a.png" } };
        var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, form, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private async Task<(IdentityClient Client, Guid UserId)> SignedInAsync(string email, CancellationToken ct)
    {
        var client = new IdentityClient(_api);
        _disposables.Add(client);
        var authenticator = new SoftwareAuthenticator();
        _disposables.Add(authenticator);
        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, email, ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        return (client, userId);
    }

    private async Task<List<string>> ListKeysAsync(Guid userId, CancellationToken ct)
    {
        var s3 = _api.Services.GetRequiredService<IAmazonS3>();
        var listing = await s3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = infrastructure.StorageBucket, Prefix = $"avatars/{userId:N}/" }, ct);
        return [.. (listing.S3Objects ?? []).Select(o => o.Key)];
    }

    private async Task<string?> CurrentKeyAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct)).Avatar?.Key;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, string description, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + EventualTimeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {description}.");
            await Task.Delay(250, ct);
        }
    }
}