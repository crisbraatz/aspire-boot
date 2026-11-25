using System.Security.Claims;

using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace AspireBoot.Api.Identity.Account.GetAvatar;

/// <summary>
/// Streams the signed-in user's avatar. The object key always comes from the caller's own database row, never from the
/// URL: the version in the path only busts caches and a mismatch is a 404. The Authenticated rate-limit policy is
/// intentional, because immutable private caching keeps repeated loads off the server.
/// </summary>
internal static class GetAvatarEndpoint
{
    private const string CacheControl = "private, max-age=31536000, immutable";

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

        if (row?.Key is null || row.AvatarVersion != version)
        {
            return TypedResults.NotFound();
        }

        var etag = $"\"v{version}\"";
        context.Response.Headers[HeaderNames.CacheControl] = CacheControl;
        context.Response.Headers[HeaderNames.ETag] = etag;

        if (context.Request.Headers.IfNoneMatch.Contains(etag))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        var stream = await storage.OpenReadAsync(row.Key, cancellationToken);
        return stream is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(stream, "image/webp");
    }
}