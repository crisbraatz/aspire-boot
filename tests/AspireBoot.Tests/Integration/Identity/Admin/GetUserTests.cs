using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Tests.Integration.Infrastructure;

namespace AspireBoot.Tests.Integration.Identity.Admin;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class GetUserTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly string[] ExpectedFields =
        ["id", "displayName", "email", "role", "emailConfirmed", "createdAt", "avatarUrl", "passkeyCount", "culture"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AdminSession.CreateFactory(infrastructure);
        using var client = new IdentityClient(api);

        using var response = await client.Http.GetAsync(UserUri(Guid.NewGuid()), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (user, userId) = await AdminSession.SignInAsync(api, infrastructure, "detail-plain@aspireboot.test", ct);
        using var _user = user;

        using var response = await user.Http.GetAsync(UserUri(userId), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_ReturnsDetail_WithPasskeyCountAndCulture_AndNoSensitiveFields()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "detail-member@aspireboot.test", ct);
        using var _member = member;

        using var response = await admin.Http.GetAsync(UserUri(memberId), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(ExpectedFields.Order(StringComparer.Ordinal), document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        var detail = JsonSerializer.Deserialize<DetailBody>(json, JsonOptions) ?? throw new InvalidOperationException("Empty detail.");
        Assert.Equal(memberId, detail.Id);
        Assert.Equal("detail-member@aspireboot.test", detail.Email);
        Assert.Equal("User", detail.Role);
        Assert.True(detail.EmailConfirmed);
        Assert.Equal(1, detail.PasskeyCount);
        Assert.Equal("pt-BR", detail.Culture);
    }

    [Fact]
    public async Task UnknownUser_Returns404Problem()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;

        using var response = await admin.Http.GetAsync(UserUri(Guid.NewGuid()), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("identity.userNotFound", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AvatarUrl_IsVersioned_WhenTheUserHasAnAvatar()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "detail-avatar@aspireboot.test", ct);
        using var _member = member;
        await AdminSession.UploadAvatarAsync(member, ct);

        using var response = await admin.Http.GetAsync(UserUri(memberId), ct);

        var detail = await response.Content.ReadFromJsonAsync<DetailBody>(JsonOptions, ct);
        Assert.Equal($"/api/identity/admin/users/{memberId:D}/avatar/1", detail?.AvatarUrl);
    }

    public ValueTask InitializeAsync()
    {
        // Touching Services starts the Worker host, which sends the queued confirmation emails.
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _worker.DisposeAsync();

    private static Uri UserUri(Guid id) => new($"/api/identity/admin/users/{id:D}", UriKind.Relative);

    private sealed record DetailBody(Guid Id, string DisplayName, string Email, string Role, bool EmailConfirmed, DateTimeOffset CreatedAt, string? AvatarUrl, int PasskeyCount, string Culture);
}