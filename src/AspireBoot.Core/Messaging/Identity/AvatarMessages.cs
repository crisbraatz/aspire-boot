namespace AspireBoot.Core.Messaging.Identity;

/// <summary>Asks the Worker to delete an avatar object that a newer upload or a removal has replaced.</summary>
public sealed record AvatarObjectSuperseded(Guid UserId, string Key);

/// <summary>Delayed check that deletes an avatar object unless it is still the user's current avatar (or the user is gone).</summary>
public sealed record AvatarObjectOrphanCheck(Guid UserId, string Key);