using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Passkey (WebAuthn) relying party settings, bound from the <c>Passkeys</c> section. Both values are required.</summary>
public sealed class PasskeyOptions
{
    public const string SectionName = "Passkeys";

    /// <summary>Effective domain of the server, without scheme or port (for example <c>app.example.com</c>).</summary>
    [Required, MinLength(1)]
    public string ServerDomain { get; init; } = string.Empty;

    /// <summary>Exact browser origins allowed to run passkey ceremonies (for example <c>https://app.example.com</c>).</summary>
    [Required, MinLength(1)]
    public string[] AllowedOrigins { get; init; } = [];
}