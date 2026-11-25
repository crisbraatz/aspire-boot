using Microsoft.AspNetCore.WebUtilities;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Public passkey identifiers (base64url credential id) and the passkey display-name rule.</summary>
internal static class PasskeyIds
{
    public const int MaxNameLength = 50;
    public const int MaxCredentialLength = 16 * 1024;
    private const int MaxEncodedIdLength = 1024;

    public static string Encode(byte[] credentialId) => WebEncoders.Base64UrlEncode(credentialId);

    /// <summary>Decodes a passkey id from a route. Anything malformed is treated as an unknown passkey.</summary>
    public static bool TryDecode(string? encoded, out byte[] credentialId)
    {
        credentialId = [];
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxEncodedIdLength)
        {
            return false;
        }

        try
        {
            credentialId = WebEncoders.Base64UrlDecode(encoded);
            return credentialId.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Trims and checks a passkey display name (1 to 50 characters).</summary>
    public static bool TryNormalizeName(string? raw, out string name)
    {
        name = raw?.Trim() ?? string.Empty;
        return name.Length is >= 1 and <= MaxNameLength;
    }
}