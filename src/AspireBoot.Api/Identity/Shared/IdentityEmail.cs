using System.ComponentModel.DataAnnotations;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Email rules shared by the identity slices: format and length check, and the normalized form used as a key.</summary>
internal static class IdentityEmail
{
    public const int MaxLength = 256;

    public static bool IsValid(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= MaxLength
        && new EmailAddressAttribute().IsValid(email);

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
}