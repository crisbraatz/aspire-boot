using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Caching.Distributed;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// Per-email cooldown for identity emails. The cache key hashes the normalized email, so no address is stored in clear.
/// A zero cooldown disables the check but still reports the flow as allowed.
/// </summary>
internal static class EmailCooldown
{
    /// <summary>Starts the cooldown for the email and returns true, or returns false when a cooldown is already active.</summary>
    public static async Task<bool> TryStartAsync(
        IDistributedCache cache, string keyPrefix, string email, int cooldownSeconds, CancellationToken cancellationToken)
    {
        var key = keyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(IdentityEmail.Normalize(email))));

        if (await cache.GetAsync(key, cancellationToken) is not null)
        {
            return false;
        }

        if (cooldownSeconds > 0)
        {
            await cache.SetAsync(
                key,
                [1],
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(cooldownSeconds) },
                cancellationToken);
        }

        return true;
    }
}