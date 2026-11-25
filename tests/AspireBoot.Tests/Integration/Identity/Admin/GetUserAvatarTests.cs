using System.Net;
using System.Net.Http.Headers;

using AspireBoot.Tests.Integration.Infrastructure;

namespace AspireBoot.Tests.Integration.Identity.Admin;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class GetUserAvatarTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AdminSession.CreateFactory(infrastructure);
        using var client = new IdentityClient(api);

        using var response = await client.Http.GetAsync(AvatarUri(Guid.NewGuid(), 1), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "avatar-plain@aspireboot.test", ct);
        using var _member = member;
        await AdminSession.UploadAvatarAsync(member, ct);

        using var response = await member.Http.GetAsync(AvatarUri(memberId, 1), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutAvatar_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "avatar-none@aspireboot.test", ct);
        using var _member = member;

        using var response = await admin.Http.GetAsync(AvatarUri(memberId, 1), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_ReadsTheTargetAvatar_WithPrivateImmutableCache()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "avatar-admin@aspireboot.test", ct);
        using var _member = member;
        await AdminSession.UploadAvatarAsync(member, ct);

        using var response = await admin.Http.GetAsync(AvatarUri(memberId, 1), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("private, max-age=31536000, immutable", response.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(ct));
    }

    [Fact]
    public async Task MatchingIfNoneMatch_Returns304()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "avatar-etag@aspireboot.test", ct);
        using var _member = member;
        await AdminSession.UploadAvatarAsync(member, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, AvatarUri(memberId, 1));
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"v1\""));

        using var response = await admin.Http.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    [Fact]
    public async Task StaleOrFutureVersion_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "avatar-stale@aspireboot.test", ct);
        using var _member = member;
        await AdminSession.UploadAvatarAsync(member, ct);
        await AdminSession.UploadAvatarAsync(member, ct);

        using var stale = await admin.Http.GetAsync(AvatarUri(memberId, 1), ct);
        using var future = await admin.Http.GetAsync(AvatarUri(memberId, 3), ct);
        using var current = await admin.Http.GetAsync(AvatarUri(memberId, 2), ct);

        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, future.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    public ValueTask InitializeAsync()
    {
        // Touching Services starts the Worker host, which sends the queued confirmation emails.
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _worker.DisposeAsync();

    private static Uri AvatarUri(Guid userId, int version) =>
        new($"/api/identity/admin/users/{userId:D}/avatar/{version}", UriKind.Relative);

}