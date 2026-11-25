using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Bootstrap rules, bound from the <c>Bootstrap</c> section. Set <c>Bootstrap__AdminEmail</c> to grant Admin to that address on its first confirmation.</summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    /// <summary>Email address that becomes Admin when it confirms. Empty means no bootstrap Admin.</summary>
    [EmailAddress]
    public string? AdminEmail { get; init; }
}