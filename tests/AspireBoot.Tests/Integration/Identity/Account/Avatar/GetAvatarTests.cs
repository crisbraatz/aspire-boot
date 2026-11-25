using System.Net;
using System.Net.Http.Headers;

using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

namespace AspireBoot.Tests.Integration.Identity.Account.Avatar;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class GetAvatarTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri AvatarUri = new("/api/identity/account/avatar", UriKind.Relative);
    private static readonly Uri FirstVersionUri = new("/api/identity/account/avatar/1", UriKind.Relative);

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);
    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public async Task OwnAvatar_ReturnsA256WebpWithPrivateImmutableCacheHeaders()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await SignedInAsync("avatar-get@aspireboot.test", ct);
        await UploadAsync(client, ct);

        using var response = await client.Http.GetAsync(FirstVersionUri, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("private, max-age=31536000, immutable", response.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
        using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync(ct));
        Assert.NotNull(bitmap);
        Assert.Equal(256, bitmap.Width);
        Assert.Equal(256, bitmap.Height);
    }

    [Fact]
    public async Task MatchingIfNoneMatch_Returns304()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await SignedInAsync("avatar-get-etag@aspireboot.test", ct);
        await UploadAsync(client, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, FirstVersionUri);
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"v1\""));

        using var response = await client.Http.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task StaleOrFutureVersion_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await SignedInAsync("avatar-get-stale@aspireboot.test", ct);
        await UploadAsync(client, ct);
        await UploadAsync(client, ct);

        using var stale = await client.Http.GetAsync(FirstVersionUri, ct);
        using var future = await client.Http.GetAsync(new Uri("/api/identity/account/avatar/3", UriKind.Relative), ct);
        using var current = await client.Http.GetAsync(new Uri("/api/identity/account/avatar/2", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, future.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task AfterRemoval_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await SignedInAsync("avatar-get-removed@aspireboot.test", ct);
        await UploadAsync(client, ct);
        using var removed = await client.SendUnsafeAsync(HttpMethod.Delete, AvatarUri, null, ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using var response = await client.Http.GetAsync(FirstVersionUri, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnotherUser_NeverReceivesSomeoneElsesBytes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, ownerId) = await SignedInAsync("avatar-get-owner@aspireboot.test", ct);
        var (other, otherId) = await SignedInAsync("avatar-get-other@aspireboot.test", ct);
        await UploadAsync(owner, ct);

        using var withoutAvatar = await other.Http.GetAsync(FirstVersionUri, ct);
        Assert.Equal(HttpStatusCode.NotFound, withoutAvatar.StatusCode);

        await UploadAsync(other, ct);
        using var withOwn = await other.Http.GetAsync(FirstVersionUri, ct);
        var returned = await withOwn.Content.ReadAsByteArrayAsync(ct);

        Assert.Equal(HttpStatusCode.OK, withOwn.StatusCode);
        Assert.Equal(await StoredBytesAsync(otherId, ct), returned);
        Assert.NotEqual(ownerId, otherId);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new IdentityClient(_api);

        using var response = await client.Http.GetAsync(FirstVersionUri, ct);

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

    private static async Task UploadAsync(IdentityClient client, CancellationToken ct)
    {
        var file = new ByteArrayContent(AvatarTestImages.Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { file, "file", "a.png" } };
        using var response = await client.SendUnsafeAsync(HttpMethod.Put, AvatarUri, form, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private async Task<byte[]> StoredBytesAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct)).Avatar?.Key
            ?? throw new InvalidOperationException("The user has no avatar.");
        var storage = scope.ServiceProvider.GetRequiredService<AspireBoot.Core.Infrastructure.Storage.IAvatarStorage>();
        await using var stream = await storage.OpenReadAsync(key, ct) ?? throw new InvalidOperationException("Object missing.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}