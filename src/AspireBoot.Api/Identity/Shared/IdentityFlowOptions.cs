using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Timing rules for identity flows, bound from the <c>Identity</c> section.</summary>
public sealed class IdentityFlowOptions
{
    public const string SectionName = "Identity";

    /// <summary>Minimum seconds between two emails sent to the same account.</summary>
    [Range(0, 3600)]
    public int EmailCooldownSeconds { get; init; } = 60;
}