using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// Checks, on every request, that the cookie's security stamp is still the account's current stamp (ADR 0005). It replaces
/// Identity's interval-based <c>SecurityStampValidator</c>. Fail safe: when the cache fails the database is read directly,
/// and when the database fails too the principal is rejected, never accepted.
/// </summary>
internal sealed partial class SecurityStampPrincipalValidator(
    ISecurityStampCache cache,
    IOptions<IdentityOptions> identityOptions,
    ILogger<SecurityStampPrincipalValidator> logger)
{
    /// <summary>Cookie event handler: rejects the principal and ends the cookie session when the stamp does not match.</summary>
    public static async Task OnValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var validator = context.HttpContext.RequestServices.GetRequiredService<SecurityStampPrincipalValidator>();
        if (await validator.IsValidAsync(context.Principal, context.HttpContext.RequestAborted))
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    public async Task<bool> IsValidAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var options = identityOptions.Value.ClaimsIdentity;
        var rawUserId = principal?.FindFirstValue(options.UserIdClaimType);
        var cookieStamp = principal?.FindFirstValue(options.SecurityStampClaimType);

        if (!Guid.TryParse(rawUserId, out var userId) || string.IsNullOrEmpty(cookieStamp))
        {
            MissingClaims(logger);
            return false;
        }

        string? current;
        try
        {
            current = await cache.GetStampAsync(userId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CacheFailed(logger, exception.GetType().Name);
            try
            {
                current = await cache.GetStampFromDatabaseAsync(userId, cancellationToken);
            }
            catch (Exception databaseException) when (databaseException is not OperationCanceledException)
            {
                DatabaseFailed(logger, databaseException.GetType().Name);
                return false;
            }
        }

        if (string.IsNullOrEmpty(current) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current), Encoding.UTF8.GetBytes(cookieStamp)))
        {
            StampRejected(logger, userId);
            return false;
        }

        return true;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Session rejected: the cookie has no user id or security stamp.")]
    private static partial void MissingClaims(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Session rejected for user {UserId}: the security stamp changed or the account is gone.")]
    private static partial void StampRejected(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Security stamp cache failed ({ErrorType}); reading the database directly.")]
    private static partial void CacheFailed(ILogger logger, string errorType);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Security stamp lookup failed in the database ({ErrorType}); session rejected.")]
    private static partial void DatabaseFailed(ILogger logger, string errorType);
}