using System.Text;

using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// Validates the single-use tokens carried by identity email links. Failures use generic codes so the
/// response never reveals whether an account exists or why a link was rejected.
/// </summary>
internal static class IdentityLinkTokens
{
    public const string ConfirmPurpose = "confirm";
    public const string RecoverPurpose = "recover";

    public const string InvalidLinkCode = "identity.invalidLink";
    public const string PasskeyFailedCode = "identity.passkeyFailed";
    public const string SignInFailedCode = "identity.signInFailed";

    private const string EmailConfirmationPurpose = "EmailConfirmation";
    public const string PasskeyRecoveryPurpose = "PasskeyRecovery";
    private const int MaxEncodedTokenLength = 4096;

    /// <summary>Decodes the base64url token from the link query string back to the raw Identity token.</summary>
    public static bool TryDecode(string? encoded, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxEncodedTokenLength)
        {
            return false;
        }

        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
            return token.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the user when the token is valid for the purpose: "confirm" needs an unconfirmed account and the
    /// email confirmation token; "recover" needs a confirmed account and the passkey recovery token.
    /// </summary>
    public static async Task<User?> FindUserForPurposeAsync(UserManager<User> users, Guid userId, string purpose, string token)
    {
        var user = await users.FindByIdAsync(userId.ToString("D"));
        if (user is null)
        {
            return null;
        }

        switch (purpose)
        {
            case ConfirmPurpose:
                return !user.EmailConfirmed
                    && await users.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, EmailConfirmationPurpose, token)
                        ? user
                        : null;

            case RecoverPurpose:
                return user.EmailConfirmed
                    && await users.VerifyUserTokenAsync(user, PasskeyRecoveryTokenProvider<User>.ProviderName, PasskeyRecoveryPurpose, token)
                        ? user
                        : null;

            default:
                return null;
        }
    }

    public static ProblemHttpResult InvalidLink() =>
        Problem(InvalidLinkCode, "The link is invalid or has expired. Request a new one.");

    public static ProblemHttpResult PasskeyFailed() =>
        Problem(PasskeyFailedCode, "The passkey could not be verified. Try again with a different device.");

    /// <summary>Every sign-in failure looks the same: no hint about the account, the credential or the cause.</summary>
    public static ProblemHttpResult SignInFailed() =>
        Problem(SignInFailedCode, "Sign-in failed. Check your passkey and try again.", StatusCodes.Status401Unauthorized);

    private static ProblemHttpResult Problem(string code, string title, int statusCode = StatusCodes.Status400BadRequest) =>
        TypedResults.Problem(
            title: title,
            statusCode: statusCode,
            type: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}