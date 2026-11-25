using System.Text.Json;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Auth.SignIn;

public sealed record SignInRequest(JsonElement? Credential, bool KeepSignedIn);

/// <summary>
/// Signs in with a passkey. The assertion is verified first; <c>keepSignedIn</c> chooses a persistent or a
/// session cookie. Every failure (bad credential, unknown or unconfirmed account) returns the same 401.
/// </summary>
internal sealed partial class SignInEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/signin", Handle)
            .WithName("SignInWithPasskey")
            .WithSummary("Signs in with a passkey. keepSignedIn chooses a persistent session cookie.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PasskeyCeremony);

    internal static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        SignInRequest request,
        UserManager<User> users,
        SignInManager<User> signIn,
        ILogger<SignInEndpoint> logger)
    {
        if (request.Credential is not { ValueKind: JsonValueKind.Object } credential)
        {
            return Rejected(logger);
        }

        var credentialJson = credential.GetRawText();
        if (credentialJson.Length > PasskeyIds.MaxCredentialLength)
        {
            return Rejected(logger);
        }

        // Identity has no persistence flag on PasskeySignInAsync, so the assertion is run here and the cookie
        // is issued with SignInAsync(user, keepSignedIn), as recorded in T2.
        var assertion = await signIn.PerformPasskeyAssertionAsync(credentialJson);
        if (!assertion.Succeeded || assertion.User is not { } user || !user.EmailConfirmed)
        {
            return Rejected(logger);
        }

        // Stores the signature counter the assertion advanced, so clone detection keeps working.
        if (assertion.Passkey is { } passkey)
        {
            var saved = await users.AddOrUpdatePasskeyAsync(user, passkey);
            if (!saved.Succeeded)
            {
                return Rejected(logger);
            }
        }

        await signIn.SignInAsync(user, request.KeepSignedIn);

        IdentityMetrics.RecordSignIn(success: true);
        SignedIn(logger, user.Id);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult Rejected(ILogger logger)
    {
        IdentityMetrics.RecordSignIn(success: false);
        SignInRejected(logger);
        return IdentityLinkTokens.SignInFailed();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Signed in user {UserId}.")]
    private static partial void SignedIn(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Passkey sign-in refused.")]
    private static partial void SignInRejected(ILogger logger);
}