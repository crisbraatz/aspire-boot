using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// A write that UserManager reported as failed. The caller returns this before committing, so the transaction rolls back.
/// Identity error details stay in the logs and are never sent to the client.
/// </summary>
internal static class IdentityOperationFailure
{
    public const string Code = "identity.operationFailed";

    public static ProblemHttpResult Problem() =>
        TypedResults.Problem(
            title: "The operation could not be completed.",
            statusCode: StatusCodes.Status500InternalServerError,
            type: Code,
            extensions: new Dictionary<string, object?> { ["code"] = Code });

    /// <summary>Identity error codes (never descriptions, which can echo user input) for logging.</summary>
    public static string Codes(IdentityResult result) => string.Join(',', result.Errors.Select(error => error.Code));
}