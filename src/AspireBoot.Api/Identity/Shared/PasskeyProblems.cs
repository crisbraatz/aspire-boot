using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Problem Details for passkey management. Codes are stable and translatable; details never leak internals.</summary>
internal static class PasskeyProblems
{
    public const string NotFoundCode = "identity.passkeyNotFound";
    public const string LastPasskeyCode = "identity.lastPasskey";

    public static ProblemHttpResult NotFound() =>
        Problem(NotFoundCode, "The passkey was not found.", StatusCodes.Status404NotFound);

    public static ProblemHttpResult LastPasskey() =>
        Problem(LastPasskeyCode, "The last passkey of an account cannot be removed.", StatusCodes.Status409Conflict);

    private static ProblemHttpResult Problem(string code, string title, int statusCode) =>
        TypedResults.Problem(
            title: title,
            statusCode: statusCode,
            type: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}