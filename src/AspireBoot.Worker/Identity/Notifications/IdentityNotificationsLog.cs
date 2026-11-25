using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Notifications;

/// <summary>Source-generated logs for identity notifications. Only the opaque user ID is logged: never the email, name, link or passkey name.</summary>
internal static partial class IdentityNotificationsLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Identity notification {NotificationKind} sent to user {UserId}")]
    public static partial void NotificationSent(ILogger logger, string NotificationKind, Guid UserId);
}