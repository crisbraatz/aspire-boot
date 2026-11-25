using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace AspireBoot.Api.Identity.Auth.SignOut;

/// <summary>Ends the current session and clears the session cookie. Requires an authenticated caller (fallback policy).</summary>
internal static class SignOutEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/signout", Handle)
            .WithName("SignOut")
            .WithSummary("Ends the current session.")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<NoContent> Handle(SignInManager<User> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }
}