using System.Text;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>Builds the links that identity emails carry. The token travels base64url-encoded in the query string.</summary>
internal static class IdentityLinks
{
    public const string PublicBaseUrlKey = "App:PublicBaseUrl";

    public static string Confirmation(IConfiguration configuration, Guid userId, string token)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(token);

        var baseUrl = configuration[PublicBaseUrlKey]?.TrimEnd('/')
            ?? throw new InvalidOperationException($"{PublicBaseUrlKey} is not configured.");
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        return $"{baseUrl}/confirm?u={userId:D}&t={encodedToken}";
    }

    public static string Recovery(IConfiguration configuration, Guid userId, string token)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(token);

        var baseUrl = configuration[PublicBaseUrlKey]?.TrimEnd('/')
            ?? throw new InvalidOperationException($"{PublicBaseUrlKey} is not configured.");
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        return $"{baseUrl}/recover/complete?u={userId:D}&t={encodedToken}";
    }
}