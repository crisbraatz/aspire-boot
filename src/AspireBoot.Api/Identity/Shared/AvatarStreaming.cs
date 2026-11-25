using AspireBoot.Core.Infrastructure.Storage;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// Serves one stored avatar for both the self-service and the administrator endpoints. Each caller resolves the
/// object key from the target user's own database row (never from the URL); this part decides the response: a
/// version mismatch or a missing object is 404, the ETag is the version, and a matching If-None-Match is 304.
/// The cache is private and immutable because every upload bumps the version.
/// </summary>
internal static class AvatarStreaming
{
    internal const string CacheControl = "private, max-age=31536000, immutable";

    public static async Task<Results<FileStreamHttpResult, StatusCodeHttpResult, NotFound>> ServeAsync(
        HttpContext context,
        string? key,
        int avatarVersion,
        int requestedVersion,
        IAvatarStorage storage,
        CancellationToken cancellationToken)
    {
        if (key is null || avatarVersion != requestedVersion)
        {
            return TypedResults.NotFound();
        }

        var etag = $"\"v{requestedVersion}\"";
        context.Response.Headers[HeaderNames.CacheControl] = CacheControl;
        context.Response.Headers[HeaderNames.ETag] = etag;

        if (context.Request.Headers.IfNoneMatch.Contains(etag))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        var stream = await storage.OpenReadAsync(key, cancellationToken);
        return stream is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(stream, "image/webp");
    }

    /// <summary>The versioned administrator URL of a user's avatar, or null when the user shows initials.</summary>
    public static string? AdminAvatarUrl(Guid userId, string? key, int avatarVersion) =>
        key is null ? null : $"/api/identity/admin/users/{userId:D}/avatar/{avatarVersion}";
}