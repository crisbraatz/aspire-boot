using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Tests.Integration.Infrastructure;

namespace AspireBoot.Tests.Integration.Identity.Admin;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class ListUsersTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri ListUri = new("/api/identity/admin/users", UriKind.Relative);
    private static readonly string[] ExpectedFields = ["id", "displayName", "email", "role", "emailConfirmed", "createdAt", "avatarUrl"];

    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = AdminSession.CreateFactory(infrastructure);
        using var client = new IdentityClient(api);

        using var response = await client.Http.GetAsync(ListUri, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (user, _) = await AdminSession.SignInAsync(api, infrastructure, "list-plain@aspireboot.test", ct);
        using var _user = user;

        using var response = await user.Http.GetAsync(ListUri, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_ReturnsNewestFirst_WithOnlyTheListFields()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (first, _) = await AdminSession.SignInAsync(api, infrastructure, "list-first@aspireboot.test", ct);
        using var _first = first;

        using var response = await admin.Http.GetAsync(ListUri, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(ExpectedFields.Order(StringComparer.Ordinal), item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        var body = JsonSerializer.Deserialize<PageBody>(json, JsonOptions) ?? throw new InvalidOperationException("Empty page.");
        Assert.Equal(2, body.Total);
        Assert.Equal(1, body.Page);
        Assert.Equal(20, body.PageSize);
        Assert.Equal(["list-first@aspireboot.test", AdminSession.AdminEmail], body.Items.Select(i => i.Email));
        Assert.Equal(["User", "Admin"], body.Items.Select(i => i.Role));
        Assert.All(body.Items, item => Assert.True(item.EmailConfirmed));
        Assert.All(body.Items, item => Assert.Null(item.AvatarUrl));
    }

    [Fact]
    public async Task Search_MatchesEmailCaseInsensitively_AndTreatsWildcardsAsLiterals()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (alpha, _) = await AdminSession.SignInAsync(api, infrastructure, "search-alpha@aspireboot.test", ct);
        using var _alpha = alpha;
        var (beta, _) = await AdminSession.SignInAsync(api, infrastructure, "search-beta@aspireboot.test", ct);
        using var _beta = beta;

        var byEmail = await GetPageAsync(admin, "?search=ALPHA", ct);
        var percent = await GetPageAsync(admin, "?search=%25", ct);
        var underscore = await GetPageAsync(admin, "?search=_", ct);

        Assert.Equal(["search-alpha@aspireboot.test"], byEmail.Items.Select(i => i.Email));
        Assert.Empty(percent.Items);
        Assert.Empty(underscore.Items);
    }

    [Fact]
    public async Task RoleFilter_ReturnsOnlyTheRequestedRole()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, _) = await AdminSession.SignInAsync(api, infrastructure, "role-member@aspireboot.test", ct);
        using var _member = member;

        var admins = await GetPageAsync(admin, "?role=Admin", ct);
        var users = await GetPageAsync(admin, "?role=User", ct);

        Assert.Equal([AdminSession.AdminEmail], admins.Items.Select(i => i.Email));
        Assert.Equal(["role-member@aspireboot.test"], users.Items.Select(i => i.Email));
    }

    [Fact]
    public async Task Paging_ReturnsTheRequestedSlice_WithTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (second, _) = await AdminSession.SignInAsync(api, infrastructure, "page-second@aspireboot.test", ct);
        using var _second = second;
        var (third, _) = await AdminSession.SignInAsync(api, infrastructure, "page-third@aspireboot.test", ct);
        using var _third = third;

        var lastPage = await GetPageAsync(admin, "?page=2&pageSize=2", ct);
        var beyond = await GetPageAsync(admin, "?page=3&pageSize=2", ct);

        Assert.Equal(["admin-console@aspireboot.test"], lastPage.Items.Select(i => i.Email));
        Assert.Equal(3, lastPage.Total);
        Assert.Equal(2, lastPage.PageSize);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.Total);
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=0")]
    [InlineData("?role=Owner")]
    [InlineData("?search=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task InvalidQuery_Returns400(string query)
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;

        using var response = await admin.Http.GetAsync(new Uri(ListUri + query, UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AvatarUrl_IsVersioned_WhenTheUserHasAnAvatar_AndNullOtherwise()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (withAvatar, withAvatarId) = await AdminSession.SignInAsync(api, infrastructure, "list-avatar@aspireboot.test", ct);
        using var _withAvatar = withAvatar;
        await AdminSession.UploadAvatarAsync(withAvatar, ct);

        var page = await GetPageAsync(admin, string.Empty, ct);

        Assert.Equal($"/api/identity/admin/users/{withAvatarId:D}/avatar/1", page.Items.Single(i => i.Email == "list-avatar@aspireboot.test").AvatarUrl);
        Assert.Null(page.Items.Single(i => i.Email == AdminSession.AdminEmail).AvatarUrl);
    }

    public ValueTask InitializeAsync()
    {
        // Touching Services starts the Worker host, which sends the queued confirmation emails.
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _worker.DisposeAsync();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<PageBody> GetPageAsync(IdentityClient admin, string query, CancellationToken ct)
    {
        using var response = await admin.Http.GetAsync(new Uri(ListUri + query, UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<PageBody>(JsonOptions, ct) ?? throw new InvalidOperationException("Empty page.");
    }

    private sealed record PageBody(List<ItemBody> Items, int Page, int PageSize, int Total);

    private sealed record ItemBody(Guid Id, string DisplayName, string Email, string Role, bool EmailConfirmed, DateTimeOffset CreatedAt, string? AvatarUrl);
}