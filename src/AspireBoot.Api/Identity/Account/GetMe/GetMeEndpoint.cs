using System.Security.Claims;

using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Account.GetMe;

public sealed record MeResponse(Guid Id, string DisplayName, string Email, string Culture);

/// <summary>Returns the signed-in user's own profile. Read-only, no tracking, one row.</summary>
internal static class GetMeEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/me", Handle)
            .WithName("GetCurrentUser")
            .WithSummary("Returns the signed-in user's profile.")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> Handle(
        ClaimsPrincipal principal,
        UserManager<User> users,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(users.GetUserId(principal), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var row = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.Id, user.DisplayName, user.Email, user.PreferredCulture })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new MeResponse(row.Id, row.DisplayName.Value, row.Email ?? string.Empty, row.PreferredCulture.Value));
    }
}