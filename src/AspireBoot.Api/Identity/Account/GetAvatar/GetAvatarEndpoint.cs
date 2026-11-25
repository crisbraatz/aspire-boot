using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Account.GetAvatar;

/// <summary>
/// Streams the signed-in user's avatar. The object key always comes from the caller's own database row, never from the
/// URL: the version in the path only busts caches and a mismatch is a 404. The Authenticated rate-limit policy is
/// intentional, because immutable private caching keeps repeated loads off the server.
/// </summary>
internal static class GetAvatarEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/avatar/{version:int}", Handle)
            .WithName("GetAvatar")
            .WithSummary("Streams the signed-in user's current avatar as image/webp. A stale version returns 404.")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<FileStreamHttpResult, StatusCodeHttpResult, NotFound, UnauthorizedHttpResult>> Handle(
        int version,
        HttpContext context,
        ClaimsPrincipal principal,
        UserManager<User> users,
        AppDbContext db,
        IAvatarStorage storage,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(users.GetUserId(principal), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var row = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { Key = EF.Property<string?>(user, "avatarKey"), user.AvatarVersion })
            .FirstOrDefaultAsync(cancellationToken);

        var served = await AvatarStreaming.ServeAsync(context, row?.Key, row?.AvatarVersion ?? 0, version, storage, cancellationToken);
        return served.Result switch
        {
            FileStreamHttpResult stream => stream,
            StatusCodeHttpResult status => status,
            _ => TypedResults.NotFound(),
        };
    }
}