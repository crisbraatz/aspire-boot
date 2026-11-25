using System.Net;
using System.Net.Http.Json;

using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace AspireBoot.Tests.Integration.Identity.Admin;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class ChangeUserRoleTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly Uri MeUri = new("/api/identity/account/me", UriKind.Relative);

    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task Anonymous_Returns401_AndNonAdmin_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        using var anonymous = new IdentityClient(api);
        var (user, userId) = await AdminSession.SignInAsync(api, infrastructure, "role-plain@aspireboot.test", ct);
        using var _user = user;

        using var anonymousResponse = await anonymous.SendUnsafeAsync(HttpMethod.Put, RoleUri(userId), Body("Admin"), ct);
        using var forbidden = await user.SendUnsafeAsync(HttpMethod.Put, RoleUri(userId), Body("Admin"), ct);

        Assert.Contains(anonymousResponse.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Promote_Returns204_AndTheUserBecomesAdmin_WithAuditAndSingleRole()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "role-promote@aspireboot.test", ct);
        using var _member = member;

        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("Admin"), ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["Admin"], await RolesOfAsync(memberId, ct));
        Assert.True(await ScalarAsync($"select count(*) from audit_entries where table_name = 'AspNetUserRoles' and entity_key like '%{memberId}%'", ct) > 0);
    }

    [Fact]
    public async Task Demote_Returns204_AndTheUserBecomesUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "role-demote@aspireboot.test", ct);
        using var _member = member;
        using (var promote = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("Admin"), ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);
        }

        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("User"), ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["User"], await RolesOfAsync(memberId, ct));
    }

    [Fact]
    public async Task SameRole_Returns204_WithoutChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "role-same@aspireboot.test", ct);
        using var _member = member;

        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("User"), ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["User"], await RolesOfAsync(memberId, ct));
        using var stillSignedIn = await member.Http.GetAsync(MeUri, ct);
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
    }

    [Fact]
    public async Task SelfDemotion_Returns409()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, adminId) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;

        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(adminId), Body("User"), ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "identity.selfDemotion", ct);
        Assert.Equal(["Admin"], await RolesOfAsync(adminId, ct));
    }

    [Fact]
    public async Task AdminDemotesAnotherAdmin_ThenTheRemainingAdminCannotRemoveTheLastAdminRole()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, adminId) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId, memberKey) = await AdminSession.SignInKeepingAuthenticatorAsync(api, infrastructure, "role-last@aspireboot.test", ct);
        using var _member = member;
        using var _key = memberKey;
        using (var promote = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("Admin"), ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);
        }

        await AdminSession.ReSignInAsync(member, memberKey, ct);

        // The promoted member demotes the bootstrap admin (allowed: two admins), then the sole admin cannot be demoted by anyone else.
        using var demoteFirst = await member.SendUnsafeAsync(HttpMethod.Put, RoleUri(adminId), Body("User"), ct);
        Assert.Equal(HttpStatusCode.NoContent, demoteFirst.StatusCode);
        Assert.Equal(["Admin"], await RolesOfAsync(memberId, ct));

        // The remaining Admin is the only one, so only a self-demotion can target it. The lastAdmin rule itself is proven by the
        // parallel test and the domain unit tests, because an actor is always an Admin (the policy) and so is counted.
        using var self = await member.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("User"), ct);
        await AssertProblemAsync(self, HttpStatusCode.Conflict, "identity.selfDemotion", ct);
    }

    [Fact]
    public async Task FailedRoleAssignment_Returns500_AndRollsBackTheRoleRemoval()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = new RoleFailureApiFactory(infrastructure, new Dictionary<string, string?> { ["Bootstrap:AdminEmail"] = AdminSession.AdminEmail });
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "role-failure@aspireboot.test", ct);
        using var _member = member;
        api.FailRoleAssignment = true;

        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("Admin"), ct);

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "identity.operationFailed", ct);
        Assert.Equal(["User"], await RolesOfAsync(memberId, ct));
        using var stillSignedIn = await member.Http.GetAsync(MeUri, ct);
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
    }

    [Fact]
    public async Task UnknownRole_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "role-invalid@aspireboot.test", ct);
        using var _member = member;

        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("Root"), ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "identity.invalidRole", ct);
    }

    [Fact]
    public async Task UnknownUser_Returns404()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;

        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(Guid.NewGuid()), Body("Admin"), ct);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "identity.userNotFound", ct);
    }

    [Fact]
    public async Task TwoParallelDemotions_LeaveExactlyOneAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (first, firstId) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _first = first;
        var (second, secondId, secondKey) = await AdminSession.SignInKeepingAuthenticatorAsync(api, infrastructure, "role-parallel@aspireboot.test", ct);
        using var _second = second;
        using var _key = secondKey;
        using (var promote = await first.SendUnsafeAsync(HttpMethod.Put, RoleUri(secondId), Body("Admin"), ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);
        }

        await AdminSession.ReSignInAsync(second, secondKey, ct);

        var results = await Task.WhenAll(
            first.SendUnsafeAsync(HttpMethod.Put, RoleUri(secondId), Body("User"), ct),
            second.SendUnsafeAsync(HttpMethod.Put, RoleUri(firstId), Body("User"), ct));
        using var r1 = results[0];
        using var r2 = results[1];

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        // The loser is refused with lastAdmin, or with 401/403 when the winner's commit already revoked or demoted its session.
        Assert.Single(results, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Equal(1, await AdminCountAsync(ct));
    }

    [Fact]
    public async Task ParallelPromoteAndDemote_NeverLeaveZeroAdmins()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (first, firstId) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _first = first;
        var (second, secondId, secondKey) = await AdminSession.SignInKeepingAuthenticatorAsync(api, infrastructure, "role-pd-second@aspireboot.test", ct);
        using var _second = second;
        using var _key = secondKey;
        var (_, thirdId) = await AdminSession.SignInAsync(api, infrastructure, "role-pd-third@aspireboot.test", ct);
        using (var promote = await first.SendUnsafeAsync(HttpMethod.Put, RoleUri(secondId), Body("Admin"), ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);
        }

        await AdminSession.ReSignInAsync(second, secondKey, ct);

        var results = await Task.WhenAll(
            second.SendUnsafeAsync(HttpMethod.Put, RoleUri(firstId), Body("User"), ct),
            first.SendUnsafeAsync(HttpMethod.Put, RoleUri(thirdId), Body("Admin"), ct));
        foreach (var result in results)
        {
            result.Dispose();
        }

        Assert.True(await AdminCountAsync(ct) >= 1);
    }

    [Fact]
    public async Task TargetsExistingSession_Gets401OnItsNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await using var api = AdminSession.CreateFactory(infrastructure);
        var (admin, _) = await AdminSession.SignInAsync(api, infrastructure, AdminSession.AdminEmail, ct);
        using var _admin = admin;
        var (member, memberId) = await AdminSession.SignInAsync(api, infrastructure, "role-revoked@aspireboot.test", ct);
        using var _member = member;
        using (var before = await member.Http.GetAsync(MeUri, ct))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        using var response = await admin.SendUnsafeAsync(HttpMethod.Put, RoleUri(memberId), Body("Admin"), ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var after = await member.Http.GetAsync(MeUri, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    public ValueTask InitializeAsync()
    {
        // Touching Services starts the Worker host, which sends the queued confirmation emails.
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _worker.DisposeAsync();

    private static Uri RoleUri(Guid id) => new($"/api/identity/admin/users/{id:D}/role", UriKind.Relative);

    private static JsonContent Body(string role) => JsonContent.Create(new { role });

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken ct)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    private async Task ResetAsync(CancellationToken ct)
    {
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
    }

    private async Task<string[]> RolesOfAsync(Guid userId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "select r.name from \"AspNetUserRoles\" ur join \"AspNetRoles\" r on r.id = ur.role_id where ur.user_id = @id order by r.name", connection);
        command.Parameters.AddWithValue("id", userId);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return [.. names];
    }

    private Task<long> AdminCountAsync(CancellationToken ct) =>
        ScalarAsync("select count(*) from \"AspNetUserRoles\" where role_id = '0198a000-0000-7000-8000-000000000001'", ct);

    private async Task<long> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(infrastructure.PostgresConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(ct) ?? 0L);
    }
}