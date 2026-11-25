using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Amazon.S3;
using Amazon.S3.Model;

using AspireBoot.Core.Infrastructure.Images;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace AspireBoot.Tests.Integration.Identity.Account.Avatar;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class UploadAvatarTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri AvatarUri = new("/api/identity/account/avatar", UriKind.Relative);
    private static readonly TimeSpan EventualTimeout = TimeSpan.FromSeconds(45);
    private static readonly Dictionary<string, string?> ShortOrphanDelay = new() { ["Avatars:OrphanCheckDelay"] = "00:00:05" };

    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task ValidImage_WithHeaderOnlyAntiforgeryToken_Returns200AndStoresTheProcessedObject(string kind, string contentType)
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, $"avatar-ok-{kind}@aspireboot.test", ct);
        var bytes = kind switch { "jpeg" => AvatarTestImages.Jpeg(), "png" => AvatarTestImages.Png(), _ => AvatarTestImages.Webp() };

        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(bytes, $"photo.{kind}", contentType), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("/api/identity/account/avatar/1", body.RootElement.GetProperty("avatarUrl").GetString());
        var keys = await ListKeysAsync(api, userId, ct);
        var key = Assert.Single(keys);
        Assert.Equal(key, await CurrentKeyAsync(api, userId, ct));
        var stored = await OpenAsync(api, key, ct);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(stored[..4]));
    }

    [Fact]
    public async Task MissingAntiforgeryToken_Returns400_AndStoresNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-noxsrf@aspireboot.test", ct);

        using var response = await client.Http.PutAsync(AvatarUri, Form(AvatarTestImages.Png(), "a.png", "image/png"), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStoredAsync(api, userId, ct);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        using var client = new IdentityClient(api);

        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.Png(), "a.png", "image/png"), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SpoofedPngExtension_Returns415_AndStoresNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-spoof@aspireboot.test", ct);

        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.SpoofedText(), "evil.png", "image/png"), ct);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "avatar.unsupportedType", ct);
        await AssertNothingStoredAsync(api, userId, ct);
    }

    [Fact]
    public async Task CorruptImage_Returns400_AndStoresNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-corrupt@aspireboot.test", ct);

        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.CorruptJpeg(), "a.jpg", "image/jpeg"), ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "avatar.corrupt", ct);
        await AssertNothingStoredAsync(api, userId, ct);
    }

    [Fact]
    public async Task DecompressionBombHeader_Returns400_AndStoresNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-bomb@aspireboot.test", ct);

        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.DecompressionBomb(9000, 9000), "bomb.png", "image/png"), ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "avatar.dimensionsTooLarge", ct);
        await AssertNothingStoredAsync(api, userId, ct);
    }

    [Fact]
    public async Task MissingFileField_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-missing@aspireboot.test", ct);
        using var content = new MultipartFormDataContent { { new StringContent("x"), "other" } };

        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, content, ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "avatar.missing", ct);
        await AssertNothingStoredAsync(api, userId, ct);
    }

    [Fact]
    public async Task NonMultipartBody_Returns415()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-json@aspireboot.test", ct);
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, content, ct);

        // The declared multipart content type is matched at routing, before the handler runs.
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertNothingStoredAsync(api, userId, ct);
    }

    [Fact]
    public async Task FileOverFiveMegabytes_Returns413_AndStoresNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-big@aspireboot.test", ct);

        using var response = await client.SendUnsafeAsync(
            HttpMethod.Put, AvatarUri, Form(AvatarTestImages.Oversized(5_242_881), "big.jpg", "image/jpeg"), ct);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "avatar.tooLarge", ct);
        await AssertNothingStoredAsync(api, userId, ct);
    }

    [Fact]
    public async Task BodyOverSixMebibytes_IsRejectedWith413ByTheMiddleware_AndStoresNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-huge@aspireboot.test", ct);

        using var response = await client.SendUnsafeAsync(
            HttpMethod.Put, AvatarUri, Form(AvatarTestImages.Oversized(7 * 1024 * 1024), "huge.jpg", "image/jpeg"), ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertNothingStoredAsync(api, userId, ct);
    }

    [Fact]
    public async Task Replacing_PublishesSuperseded_AndTheWorkerDeletesTheOldObject()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-replace@aspireboot.test", ct);

        using (var first = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.Jpeg(), "a.jpg", "image/jpeg"), ct))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        var oldKey = await CurrentKeyAsync(api, userId, ct);
        using var second = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.Png(), "b.png", "image/png"), ct);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync(ct));
        Assert.Equal("/api/identity/account/avatar/2", body.RootElement.GetProperty("avatarUrl").GetString());
        var newKey = await CurrentKeyAsync(api, userId, ct);
        Assert.NotEqual(oldKey, newKey);
        await EventuallyAsync(async () => (await ListKeysAsync(api, userId, ct)).SequenceEqual([newKey!]), "the old object to be deleted", ct);
    }

    [Fact]
    public async Task TwoParallelUploads_LeaveExactlyOneCurrentAvatar_AndEveryOtherObjectIsEventuallyDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, ShortOrphanDelay));
        var (client, userId) = await SignedInAsync(api, "avatar-parallel@aspireboot.test", ct);

        var uploads = await Task.WhenAll(
            Upload(client, AvatarTestImages.Jpeg(), "a.jpg", "image/jpeg", ct),
            Upload(client, AvatarTestImages.Png(), "b.png", "image/png", ct));

        Assert.All(uploads, status => Assert.Equal(HttpStatusCode.OK, status));
        var current = await CurrentKeyAsync(api, userId, ct);
        Assert.NotNull(current);
        await EventuallyAsync(async () => (await ListKeysAsync(api, userId, ct)).SequenceEqual([current]), "all superseded objects to be deleted", ct);
        await using var db = await OpenDbAsync(api);
        Assert.Equal(2, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct)).AvatarVersion);
    }

    [Fact]
    public async Task StorageFailure_Returns503_LeavesTheProfileUnchanged_AndHasScheduledTheOrphanCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new FailingStorageApiFactory(infrastructure, new Dictionary<string, string?> { ["Avatars:OrphanCheckDelay"] = "00:30:00" }));
        var (client, userId) = await SignedInAsync(api, "avatar-storage-down@aspireboot.test", ct);

        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.Png(), "a.png", "image/png"), ct);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "avatar.storageUnavailable", ct);
        Assert.Null(await CurrentKeyAsync(api, userId, ct));
        await using var db = await OpenDbAsync(api);
        Assert.Equal(0, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct)).AvatarVersion);
        Assert.True(await OrphanCheckIsScheduledAsync(userId, TimeSpan.FromMinutes(20), ct), "The orphan check must be scheduled before the object is stored.");
    }

    [Fact]
    public async Task SaturatedProcessingLimiter_Returns503Busy_ThenRecovers()
    {
        var ct = TestContext.Current.CancellationToken;
        var api = Track(new AspireBootApiFactory(infrastructure, new Dictionary<string, string?>
        {
            ["Avatars:MaxConcurrentProcessing"] = "1",
            ["Avatars:ProcessingWaitSeconds"] = "1",
            ["Avatars:OrphanCheckDelay"] = "00:00:05",
        }));
        var (client, userId) = await SignedInAsync(api, "avatar-busy@aspireboot.test", ct);
        var limiter = api.Services.GetRequiredService<AvatarProcessingLimiter>();

        // One in-flight decode owns the only slot, so the next request cannot get one within the wait.
        Assert.True(await limiter.WaitAsync(ct));
        try
        {
            using var busy = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.Png(), "a.png", "image/png"), ct);

            await AssertProblemAsync(busy, HttpStatusCode.ServiceUnavailable, "avatar.busy", ct);
            await AssertNothingStoredAsync(api, userId, ct);
        }
        finally
        {
            limiter.Release();
        }

        using var recovered = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(AvatarTestImages.Png(), "a.png", "image/png"), ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        await infrastructure.ResetDatabaseAsync();

        // Touching Services starts the Worker, which sends the confirmation email used to register and deletes
        // superseded avatar objects. Both hosts provision the bucket at startup.
        _ = _worker.Services;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }

        await _worker.DisposeAsync();
    }

    private static async Task<HttpStatusCode> Upload(IdentityClient client, byte[] bytes, string fileName, string contentType, CancellationToken ct)
    {
        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, Form(bytes, fileName, contentType), ct);
        return response.StatusCode;
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken ct)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(code, problem.RootElement.GetProperty("type").GetString());
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

    private async Task<(IdentityClient Client, Guid UserId)> SignedInAsync(AspireBootApiFactory api, string email, CancellationToken ct)
    {
        var client = new IdentityClient(api);
        _disposables.Add(new DisposableAdapter(client));
        var authenticator = new SoftwareAuthenticator();
        _disposables.Add(new DisposableAdapter(authenticator));
        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, email, ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        return (client, userId);
    }

    private T Track<T>(T factory)
        where T : IAsyncDisposable
    {
        _disposables.Add(factory);
        return factory;
    }

    private async Task AssertNothingStoredAsync(AspireBootApiFactory api, Guid userId, CancellationToken ct)
    {
        Assert.Empty(await ListKeysAsync(api, userId, ct));
        Assert.Null(await CurrentKeyAsync(api, userId, ct));
    }

    private async Task<List<string>> ListKeysAsync(AspireBootApiFactory api, Guid userId, CancellationToken ct)
    {
        var s3 = api.Services.GetRequiredService<IAmazonS3>();
        var listing = await s3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = infrastructure.StorageBucket, Prefix = $"avatars/{userId:N}/" }, ct);
        return [.. (listing.S3Objects ?? []).Select(o => o.Key)];
    }

    private static async Task<byte[]> OpenAsync(AspireBootApiFactory api, string key, CancellationToken ct)
    {
        var storage = api.Services.GetRequiredService<AspireBoot.Core.Infrastructure.Storage.IAvatarStorage>();
        await using var stream = await storage.OpenReadAsync(key, ct) ?? throw new InvalidOperationException("Object missing.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static async Task<string?> CurrentKeyAsync(AspireBootApiFactory api, Guid userId, CancellationToken ct)
    {
        await using var db = await OpenDbAsync(api);
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct)).Avatar?.Key;
    }

    private static Task<AppDbContext> OpenDbAsync(AspireBootApiFactory api) =>
        Task.FromResult(api.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>());

    /// <summary>
    /// Looks for the persisted delayed message. Wolverine stores a message scheduled for an external queue as a
    /// "scheduled-envelope" row; its body carries the key, which embeds the user id.
    /// </summary>
    private async Task<bool> OrphanCheckIsScheduledAsync(Guid userId, TimeSpan atLeast, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM wolverine_api.wolverine_incoming_envelopes " +
            "WHERE message_type = 'scheduled-envelope' AND status = 'Scheduled' AND execution_time > now() + @ahead " +
            "AND encode(body, 'escape') LIKE @key",
            connection);
        command.Parameters.AddWithValue("ahead", atLeast);
        command.Parameters.AddWithValue("key", $"%avatars/{userId:N}/%");
        return (long)(await command.ExecuteScalarAsync(ct) ?? 0L) > 0;
    }

    private sealed class DisposableAdapter(IDisposable inner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}