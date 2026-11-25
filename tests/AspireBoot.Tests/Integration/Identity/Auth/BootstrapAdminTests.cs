using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using AspireBoot.Core.Identity.Domain;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Tests.Integration.Identity.Auth;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class BootstrapAdminTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri OptionsUri = new("/api/identity/auth/registration/options", UriKind.Relative);
    private static readonly Uri RegistrationUri = new("/api/identity/auth/registration", UriKind.Relative);
    private const string BootstrapKey = "Bootstrap:AdminEmail";

    private readonly AspireBootApiFactory _api = new(infrastructure);
    private readonly AspireBootWorkerFactory _worker = new(infrastructure);

    [Fact]
    public async Task ConfiguredEmail_BecomesAdmin_AndLosesUser_OnConfirm()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string email = "bootstrap-admin@aspireboot.test";
        await using var bootstrapped = new AspireBootApiFactory(infrastructure, Configured("BOOTSTRAP-Admin@AspireBoot.test"));
        using var client = new IdentityClient(bootstrapped);
        using var authenticator = new SoftwareAuthenticator();

        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, email, ct);

        var (isAdmin, isUser) = await RolesOfAsync(userId);
        Assert.True(isAdmin, "The configured email must be granted Admin when its address is confirmed.");
        Assert.False(isUser, "The bootstrapped Admin must no longer hold the User role.");
    }

    [Fact]
    public async Task FailedAdminGrant_Returns500_AndConfirmsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string email = "bootstrap-failure@aspireboot.test";
        await using var failing = new RoleFailureApiFactory(infrastructure, Configured(email));
        using var client = new IdentityClient(failing);
        using var authenticator = new SoftwareAuthenticator();
        using (var signUp = await IdentityFlow.SignUpAsync(client, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);
        }

        var link = IdentityFlow.ParseConfirmationLink((await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct)).Text);
        failing.FailRoleAssignment = true;

        using var options = await client.SendUnsafeAsync(
            HttpMethod.Post, OptionsUri, JsonContent.Create(new { userId = link.UserId, token = link.Token, purpose = "confirm" }), ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var complete = await client.SendUnsafeAsync(HttpMethod.Post, RegistrationUri, JsonContent.Create(new
        {
            userId = link.UserId,
            token = link.Token,
            purpose = "confirm",
            credential = JsonDocument.Parse(authenticator.CreateAttestation(await options.Content.ReadAsStringAsync(ct))).RootElement,
            name = "Meu celular",
        }), ct);

        Assert.Equal(HttpStatusCode.InternalServerError, complete.StatusCode);
        await using var scope = _api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(link.UserId);
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
        Assert.Empty(await users.GetPasskeysAsync(user));
        Assert.False(await users.IsInRoleAsync(user, Roles.Admin));
    }

    [Fact]
    public async Task OtherEmail_StaysUser_WhenAdminIsConfigured()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        await using var bootstrapped = new AspireBootApiFactory(infrastructure, Configured("bootstrap-admin@aspireboot.test"));
        using var client = new IdentityClient(bootstrapped);
        using var authenticator = new SoftwareAuthenticator();

        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "someone-else@aspireboot.test", ct);

        var (isAdmin, isUser) = await RolesOfAsync(userId);
        Assert.False(isAdmin);
        Assert.True(isUser);
    }

    [Fact]
    public async Task NoConfiguration_MeansNoAdmin_EvenForTheSameEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        using var client = new IdentityClient(_api);
        using var authenticator = new SoftwareAuthenticator();

        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, "bootstrap-admin@aspireboot.test", ct);

        var (isAdmin, isUser) = await RolesOfAsync(userId);
        Assert.False(isAdmin);
        Assert.True(isUser);
    }

    [Fact]
    public async Task UnconfirmedConfiguredEmail_IsNeverAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string email = "bootstrap-unconfirmed@aspireboot.test";
        await using var bootstrapped = new AspireBootApiFactory(infrastructure, Configured(email));
        using var client = new IdentityClient(bootstrapped);

        using var signUp = await IdentityFlow.SignUpAsync(client, email, ct);
        Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);

        await using var scope = _api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
        Assert.False(await users.IsInRoleAsync(user, Roles.Admin));
    }

    [Fact]
    public async Task RecoveryPurpose_NeverGrantsAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();
        await infrastructure.Mailpit.DeleteAllAsync(ct);
        const string email = "bootstrap-recovery@aspireboot.test";

        // The account is confirmed before the bootstrap email is configured, so only a recovery can run afterwards.
        using var owner = new IdentityClient(_api);
        using var passkeyA = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, owner, passkeyA, email, ct);
        await infrastructure.Mailpit.DeleteAllAsync(ct);

        await using var bootstrapped = new AspireBootApiFactory(infrastructure, Configured(email));
        using var anonymous = new IdentityClient(bootstrapped);
        using (var request = await IdentityFlow.RequestRecoveryAsync(anonymous, email, ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        }

        var link = IdentityFlow.ParseRecoveryLink((await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct)).Text);
        using var passkeyB = new SoftwareAuthenticator();
        using (var complete = await CompleteRecoveryAsync(anonymous, link, passkeyB, ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        }

        var (isAdmin, isUser) = await RolesOfAsync(userId);
        Assert.False(isAdmin, "A recovery must never grant Admin.");
        Assert.True(isUser);
    }

    public ValueTask InitializeAsync()
    {
        // Touching Services starts the Worker host, which sends the queued emails.
        _ = _worker.Services;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    private static Dictionary<string, string?> Configured(string email) => new() { [BootstrapKey] = email };

    private async Task<(bool IsAdmin, bool IsUser)> RolesOfAsync(Guid userId)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByIdAsync(userId.ToString("D"));
        Assert.NotNull(user);
        return (await users.IsInRoleAsync(user, Roles.Admin), await users.IsInRoleAsync(user, Roles.User));
    }

    private static async Task<HttpResponseMessage> CompleteRecoveryAsync(
        IdentityClient client, (string UserId, string Token) link, SoftwareAuthenticator authenticator, CancellationToken ct)
    {
        using var options = await client.SendUnsafeAsync(
            HttpMethod.Post, OptionsUri, JsonContent.Create(new { userId = link.UserId, token = link.Token, purpose = "recover" }), ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var creationOptions = await options.Content.ReadAsStringAsync(ct);

        return await client.SendUnsafeAsync(HttpMethod.Post, RegistrationUri, JsonContent.Create(new
        {
            userId = link.UserId,
            token = link.Token,
            purpose = "recover",
            credential = JsonDocument.Parse(authenticator.CreateAttestation(creationOptions)).RootElement,
            name = "Novo celular",
        }), ct);
    }
}