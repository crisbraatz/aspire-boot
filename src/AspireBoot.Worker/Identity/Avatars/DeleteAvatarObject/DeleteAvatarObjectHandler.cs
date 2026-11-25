using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Messaging.Identity;

using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Avatars.DeleteAvatarObject;

/// <summary>Deletes a superseded avatar object. Idempotent: storage ignores missing keys, so redelivery is harmless.</summary>
public sealed class DeleteAvatarObjectHandler
{
    public static async Task Handle(AvatarObjectSuperseded message, IAvatarStorage storage, ILogger<DeleteAvatarObjectHandler> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!AvatarKeys.BelongsTo(message.Key, message.UserId))
        {
            AvatarObjectLog.KeyRejected(logger, message.UserId);
            return;
        }

        var objectId = AvatarKeys.ObjectIdOf(message.Key);
        await storage.DeleteAsync(message.Key, cancellationToken);
        AvatarObjectLog.ObjectDeleted(logger, objectId, message.UserId);
    }
}