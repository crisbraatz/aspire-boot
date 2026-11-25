namespace AspireBoot.Core.Messaging.Identity;

/// <summary>Asks the Worker to send the email that confirms a new account. Carries the link, never the token in logs.</summary>
public sealed record ConfirmationEmailRequested(Guid UserId, string Email, string DisplayName, string Culture, string Link);

/// <summary>Asks the Worker to send the account recovery email.</summary>
public sealed record RecoveryEmailRequested(Guid UserId, string Email, string DisplayName, string Culture, string Link);

/// <summary>Tells the account owner that a passkey was added.</summary>
public sealed record PasskeyAddedNotification(Guid UserId, string Email, string DisplayName, string Culture, string PasskeyName);

/// <summary>Tells the account owner that a passkey was removed.</summary>
public sealed record PasskeyRemovedNotification(Guid UserId, string Email, string DisplayName, string Culture, string PasskeyName);

/// <summary>Tells the account owner that an administrator changed their role. NewRole is a role name (Admin or User).</summary>
public sealed record RoleChangedNotification(Guid UserId, string Email, string DisplayName, string Culture, string NewRole);