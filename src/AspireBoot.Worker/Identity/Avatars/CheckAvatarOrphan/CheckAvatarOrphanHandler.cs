using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Worker.Identity.Avatars.CheckAvatarOrphan;

/// <summary>
/// Deletes an avatar object unless it is the user's current avatar. When the user no longer exists, the object is an orphan and is deleted.
/// Reads the current key with <c>AsNoTracking</c> and a projection; the delayed scheduling lives in the upload slice.
/// </summary>
public sealed class CheckAvatarOrphanHandler
{
    public static async Task Handle(AvatarObjectOrphanCheck message, AppDbContext db, IAvatarStorage storage, ILogger<CheckAvatarOrphanHandler> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!AvatarKeys.BelongsTo(message.Key, message.UserId))
        {
            AvatarObjectLog.KeyRejected(logger, message.UserId);
            return;
        }

        var owner = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == message.UserId)
            .Select(user => new { CurrentKey = EF.Property<string?>(user, "avatarKey") })
            .SingleOrDefaultAsync(cancellationToken);

        var objectId = AvatarKeys.ObjectIdOf(message.Key);
        if (owner is not null && owner.CurrentKey == message.Key)
        {
            AvatarObjectLog.ObjectKept(logger, objectId, message.UserId);
            return;
        }

        await storage.DeleteAsync(message.Key, cancellationToken);
        AvatarObjectLog.ObjectDeleted(logger, objectId, message.UserId);
    }
}