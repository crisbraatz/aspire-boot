using AspireBoot.Api.Identity.Shared;
using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Admin.GetUserAvatar;

/// <summary>
/// Streams a user's avatar to an administrator. The object key comes from the target user's own row, never from the
/// URL. The version only busts caches: a stale version or a user without an avatar is a 404. Same response rules as
/// the self-service avatar (shared in <see cref="AvatarStreaming"/>).
/// </summary>
internal static class GetUserAvatarEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}/avatar/{version:int}", Handle)
            .WithName("GetUserAvatar")
            .WithSummary("Streams a user's avatar for administrators as image/webp. A stale version or a missing avatar returns 404.");

    internal static async Task<Results<FileStreamHttpResult, StatusCodeHttpResult, NotFound>> Handle(
        Guid id,
        int version,
        HttpContext context,
        AppDbContext db,
        IAvatarStorage storage,
        CancellationToken cancellationToken)
    {
        var row = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new { Key = EF.Property<string?>(user, "avatarKey"), user.AvatarVersion })
            .FirstOrDefaultAsync(cancellationToken);

        return await AvatarStreaming.ServeAsync(context, row?.Key, row?.AvatarVersion ?? 0, version, storage, cancellationToken);
    }
}