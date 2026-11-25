using System.Security.Claims;

using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Account.GetMe;

public sealed record MeResponse(Guid Id, string DisplayName, string Email, string Culture, string? AvatarUrl, string[] Roles);

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

        // Roles come from the signed-in principal (the cookie carries them), so no extra query runs.
        var roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Order(StringComparer.Ordinal).ToArray();

        var row = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.Id, user.DisplayName, user.Email, user.PreferredCulture, AvatarKey = EF.Property<string?>(user, "avatarKey"), user.AvatarVersion })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new MeResponse(row.Id, row.DisplayName.Value, row.Email ?? string.Empty, row.PreferredCulture.Value, row.AvatarKey is null ? null : $"/api/identity/account/avatar/{row.AvatarVersion}", roles));
    }
}