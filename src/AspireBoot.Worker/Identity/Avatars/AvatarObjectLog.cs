using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Avatars;

/// <summary>Source-generated logs for avatar cleanup. Only the user ID and the object ID (GUID part of the key) are logged, never the full key path.</summary>
internal static partial class AvatarObjectLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Avatar object {ObjectId} deleted for user {UserId}")]
    public static partial void ObjectDeleted(ILogger logger, string ObjectId, Guid UserId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Avatar object {ObjectId} kept for user {UserId}: still the current avatar")]
    public static partial void ObjectKept(ILogger logger, string ObjectId, Guid UserId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Avatar message for user {UserId} discarded: key does not belong to the user")]
    public static partial void KeyRejected(ILogger logger, Guid UserId);
}