using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Timing rules for identity flows, bound from the <c>Identity</c> section.</summary>
public sealed class IdentityFlowOptions
{
    public const string SectionName = "Identity";

    /// <summary>Minimum seconds between two emails sent to the same account.</summary>
    [Range(0, 3600)]
    public int EmailCooldownSeconds { get; init; } = 60;

    /// <summary>
    /// How often, in seconds, a session cookie is checked against the account's security stamp. A passkey removal or a
    /// recovery ends the other sessions only after this interval, so keep it short. Bound from <c>Identity:SecurityStampValidationSeconds</c>.
    /// </summary>
    [Range(1, 86400)]
    public int SecurityStampValidationSeconds { get; init; } = 60;
}