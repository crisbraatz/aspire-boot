using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Session record settings, bound from the <c>UserSessions</c> section.</summary>
public sealed class UserSessionOptions
{
    public const string SectionName = "UserSessions";

    /// <summary>Minimum time between two last-seen writes of the same session.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan LastSeenThrottle { get; init; } = TimeSpan.FromMinutes(5);
}