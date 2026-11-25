using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Account.RenamePasskey;

public sealed record RenamePasskeyRequest(string? Name);

/// <summary>Renames one of the signed-in user's passkeys. Another account's passkey is reported as not found.</summary>
internal sealed partial class RenamePasskeyEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPatch("/passkeys/{id}", Handle)
            .WithName("RenamePasskey")
            .WithSummary("Renames one of the signed-in user's passkeys.")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> Handle(
        string id,
        RenamePasskeyRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        ILogger<RenamePasskeyEndpoint> logger)
    {
        if (!PasskeyIds.TryNormalizeName(request.Name, out var name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["name"] = [$"Name must have between 1 and {PasskeyIds.MaxNameLength} characters."],
            });
        }

        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!PasskeyIds.TryDecode(id, out var credentialId))
        {
            return PasskeyProblems.NotFound();
        }

        var passkey = await users.GetPasskeyAsync(user, credentialId);
        if (passkey is null)
        {
            return PasskeyProblems.NotFound();
        }

        passkey.Name = name;
        var saved = await users.AddOrUpdatePasskeyAsync(user, passkey);
        if (!saved.Succeeded)
        {
            return IdentityLinkTokens.PasskeyFailed();
        }

        PasskeyRenamed(logger, user.Id);
        return TypedResults.NoContent();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Passkey renamed for user {UserId}.")]
    private static partial void PasskeyRenamed(ILogger logger, Guid userId);
}