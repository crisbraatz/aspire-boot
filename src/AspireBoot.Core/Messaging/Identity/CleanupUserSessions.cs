namespace AspireBoot.Core.Messaging.Identity;

/// <summary>Recurring job (daily, UTC): deletes session rows that ended or expired longer ago than the retention.</summary>
public sealed record CleanupUserSessions;