using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Integration.Infrastructure;

/// <summary>
/// Drives the sign-up, first passkey registration and passkey sign-in through the real endpoints, so slices
/// that need an account do not repeat the flow. Every call goes through <see cref="IdentityClient"/>.
/// </summary>
public static class IdentityFlow
{
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);
    private static readonly Uri SignUpUri = new("/api/identity/auth/signup", UriKind.Relative);
    private static readonly Uri SignOutUri = new("/api/identity/auth/signout", UriKind.Relative);
    private static readonly Uri RecoveryUri = new("/api/identity/auth/recovery", UriKind.Relative);
    private static readonly Uri RegistrationOptionsUri = new("/api/identity/auth/registration/options", UriKind.Relative);
    private static readonly Uri RegistrationUri = new("/api/identity/auth/registration", UriKind.Relative);
    private static readonly Uri SignInOptionsUri = new("/api/identity/auth/signin/options", UriKind.Relative);
    private static readonly Uri SignInUri = new("/api/identity/auth/signin", UriKind.Relative);
    private static readonly Regex ConfirmationLink = new(
        @"https://localhost/confirm\?u=(?<user>[0-9a-fA-F-]{36})&t=(?<token>[A-Za-z0-9_-]+)",
        RegexOptions.CultureInvariant);
    private static readonly Regex RecoveryLink = new(
        @"https://localhost/recover/complete\?u=(?<user>[0-9a-fA-F-]{36})&t=(?<token>[A-Za-z0-9_-]+)",
        RegexOptions.CultureInvariant);

    /// <summary>Signs up, confirms through the emailed link and registers the first passkey. The client ends up signed in.</summary>
    public static async Task<Guid> RegisterAsync(
        InfrastructureFixture infrastructure, IdentityClient client, SoftwareAuthenticator authenticator, string email, CancellationToken ct)
    {
        using var signUp = await client.SendUnsafeAsync(HttpMethod.Post, SignUpUri, JsonContent.Create(new { displayName = "Ana", email, culture = "pt-BR" }), ct);
        Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);

        var received = await infrastructure.Mailpit.WaitForMessageAsync(email, MailTimeout, ct);
        var match = ConfirmationLink.Match(received.Text);
        Assert.True(match.Success, "The confirmation email must carry the confirmation link.");
        var userId = match.Groups["user"].Value;
        var token = match.Groups["token"].Value;

        using var options = await client.SendUnsafeAsync(
            HttpMethod.Post, RegistrationOptionsUri, JsonContent.Create(new { userId, token, purpose = "confirm" }), ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var creationOptions = await options.Content.ReadAsStringAsync(ct);

        using var complete = await client.SendUnsafeAsync(HttpMethod.Post, RegistrationUri, JsonContent.Create(new
        {
            userId,
            token,
            purpose = "confirm",
            credential = JsonDocument.Parse(authenticator.CreateAttestation(creationOptions)).RootElement,
            name = "Meu celular",
        }), ct);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);

        return Guid.Parse(userId);
    }

    /// <summary>Uri of one passkey under /api/identity/account/passkeys.</summary>
    public static Uri PasskeyUri(string id) => new($"/api/identity/account/passkeys/{id}", UriKind.Relative);

    /// <summary>Posts a sign-up for the given email. The confirmation link arrives by email.</summary>
    public static Task<HttpResponseMessage> SignUpAsync(IdentityClient client, string email, CancellationToken ct) =>
        client.SendUnsafeAsync(HttpMethod.Post, SignUpUri, JsonContent.Create(new { displayName = "Ana", email, culture = "pt-BR" }), ct);

    /// <summary>Requests a passkey recovery link for the given email.</summary>
    public static Task<HttpResponseMessage> RequestRecoveryAsync(IdentityClient client, string email, CancellationToken ct) =>
        client.SendUnsafeAsync(HttpMethod.Post, RecoveryUri, JsonContent.Create(new { email }), ct);

    /// <summary>Extracts the user id and token from a passkey recovery email.</summary>
    public static (string UserId, string Token) ParseRecoveryLink(string text)
    {
        var match = RecoveryLink.Match(text);
        Assert.True(match.Success, "The recovery email must carry the recovery link.");
        return (match.Groups["user"].Value, match.Groups["token"].Value);
    }

    /// <summary>Signs out the current session. The antiforgery token is refreshed first because it is bound to the identity.</summary>
    public static async Task<HttpStatusCode> SignOutAsync(IdentityClient client, CancellationToken ct)
    {
        await client.RefreshAntiforgeryTokenAsync(ct);
        using var response = await client.SendUnsafeAsync(HttpMethod.Post, SignOutUri, null, ct);
        return response.StatusCode;
    }

    /// <summary>Runs the passkey sign-in ceremony with the software authenticator and returns the sign-in response.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(
        IdentityClient client, SoftwareAuthenticator authenticator, bool keepSignedIn, CancellationToken ct)
    {
        await client.RefreshAntiforgeryTokenAsync(ct);
        using var options = await client.SendUnsafeAsync(HttpMethod.Post, SignInOptionsUri, null, ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var requestOptions = await options.Content.ReadAsStringAsync(ct);

        return await client.SendUnsafeAsync(HttpMethod.Post, SignInUri, JsonContent.Create(new
        {
            credential = JsonDocument.Parse(authenticator.CreateAssertion(requestOptions)).RootElement,
            keepSignedIn,
        }), ct);
    }
}