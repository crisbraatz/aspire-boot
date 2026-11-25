using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Core.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    /// <summary>Must stay true outside Development and Testing (Mailpit speaks plain SMTP locally).</summary>
    public bool UseTls { get; init; } = true;

    public string? Username { get; init; }

    public string? Password { get; init; }

    [Required, EmailAddress]
    public string FromAddress { get; init; } = string.Empty;

    [Required]
    public string FromName { get; init; } = "AspireBoot";
}