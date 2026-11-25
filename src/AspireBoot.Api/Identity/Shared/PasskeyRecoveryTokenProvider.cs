using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// Single-use style tokens for passkey recovery links. The lifespan comes from
/// <see cref="DataProtectionTokenProviderOptions.TokenLifespan"/> (2 hours).
/// </summary>
public sealed class PasskeyRecoveryTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<DataProtectionTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class
{
    public const string ProviderName = "PasskeyRecovery";
}