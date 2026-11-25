using System.Net;
using System.Net.Http.Headers;

using AspireBoot.Tests.Integration.Identity.Account.Avatar;
using AspireBoot.Tests.Integration.Infrastructure;
using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Identity.Admin;

/// <summary>
/// Shared setup for the admin slice tests. The Admin account is the one configured as Bootstrap:AdminEmail, so it
/// is granted Admin through the real confirmation flow (no direct database writes).
/// </summary>
internal static class AdminSession
{
    public const string AdminEmail = "admin-console@aspireboot.test";

    public static AspireBootApiFactory CreateFactory(InfrastructureFixture infrastructure) =>
        new(infrastructure, new Dictionary<string, string?> { ["Bootstrap:AdminEmail"] = AdminEmail });

    /// <summary>Signs up and confirms the email through the real flow. The returned client stays signed in.</summary>
    public static async Task<(IdentityClient Client, Guid UserId)> SignInAsync(
        AspireBootApiFactory api, InfrastructureFixture infrastructure, string email, CancellationToken ct)
    {
        var client = new IdentityClient(api);
        using var authenticator = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, email, ct);
        // Unsafe requests (uploads, role changes) need an antiforgery token bound to this signed-in identity.
        await client.RefreshAntiforgeryTokenAsync(ct);
        return (client, userId);
    }

    /// <summary>Like <see cref="SignInAsync"/> but keeps the authenticator, so the member can sign in again after a role change revokes the session.</summary>
    public static async Task<(IdentityClient Client, Guid UserId, SoftwareAuthenticator Authenticator)> SignInKeepingAuthenticatorAsync(
        AspireBootApiFactory api, InfrastructureFixture infrastructure, string email, CancellationToken ct)
    {
        var client = new IdentityClient(api);
        var authenticator = new SoftwareAuthenticator();
        var userId = await IdentityFlow.RegisterAsync(infrastructure, client, authenticator, email, ct);
        await client.RefreshAntiforgeryTokenAsync(ct);
        return (client, userId, authenticator);
    }

    /// <summary>Signs in again with the passkey (a new session with the current security stamp).</summary>
    public static async Task ReSignInAsync(IdentityClient client, SoftwareAuthenticator authenticator, CancellationToken ct)
    {
        using var response = await IdentityFlow.SignInAsync(client, authenticator, keepSignedIn: false, ct);
        response.EnsureSuccessStatusCode();
        await client.RefreshAntiforgeryTokenAsync(ct);
    }

    /// <summary>Uploads a 256px avatar as the signed-in member through the account endpoint (version 1 on first upload).</summary>
    public static async Task UploadAvatarAsync(IdentityClient member, CancellationToken ct)
    {
        var file = new ByteArrayContent(AvatarTestImages.Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { file, "file", "a.png" } };
        using var response = await member.SendUnsafeAsync(HttpMethod.Put, new Uri("/api/identity/account/avatar", UriKind.Relative), form, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}