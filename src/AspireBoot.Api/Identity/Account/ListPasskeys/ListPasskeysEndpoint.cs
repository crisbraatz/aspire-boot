using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace AspireBoot.Api.Identity.Account.ListPasskeys;

public sealed record PasskeyResponse(string Id, string Name, DateTimeOffset CreatedAt);

/// <summary>Lists the signed-in user's passkeys, oldest first. Bounded to 50 entries.</summary>
internal static class ListPasskeysEndpoint
{
    public const int MaxPasskeys = 50;

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/passkeys", Handle)
            .WithName("ListPasskeys")
            .WithSummary("Lists the signed-in user's passkeys (name and creation date).")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<Ok<IReadOnlyList<PasskeyResponse>>, UnauthorizedHttpResult>> Handle(
        ClaimsPrincipal principal,
        UserManager<User> users,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var passkeys = await users.GetPasskeysAsync(user);
        IReadOnlyList<PasskeyResponse> response = passkeys
            .OrderBy(passkey => passkey.CreatedAt)
            .Take(MaxPasskeys)
            .Select(passkey => new PasskeyResponse(PasskeyIds.Encode(passkey.CredentialId), passkey.Name ?? string.Empty, passkey.CreatedAt))
            .ToList();

        return TypedResults.Ok(response);
    }
}