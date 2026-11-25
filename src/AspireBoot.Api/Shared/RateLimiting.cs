using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;

using AspireBoot.Api.Identity.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AspireBoot.Api.Shared;

/// <summary>Named rate-limit policies. Limits come from <c>RateLimiting:&lt;policy&gt;:PermitLimit</c> and <c>WindowSeconds</c>.</summary>
internal static class RateLimitPolicies
{
    public const string Signup = "signup";
    public const string EmailLink = "email-link";
    public const string PasskeyCeremony = "passkey-ceremony";
    public const string Authenticated = "authenticated";
    public const string AvatarUpload = "avatar-upload";

    /// <summary>Default for anonymous read-only endpoints (platform info) that have no stricter policy.</summary>
    public const string Public = "public";

    public static IReadOnlyList<string> All { get; } = [Signup, EmailLink, PasskeyCeremony, Authenticated, AvatarUpload, Public];
}

/// <summary>Fixed-window rate limiting partitioned by user (authenticated) or client IP (anonymous).</summary>
internal static class RateLimitingExtensions
{
    private const string RateLimitCode = "rateLimited";

    private static readonly IReadOnlyDictionary<string, (int PermitLimit, int WindowSeconds)> Defaults =
        new Dictionary<string, (int PermitLimit, int WindowSeconds)>(StringComparer.Ordinal)
        {
            [RateLimitPolicies.Signup] = (5, 600),
            [RateLimitPolicies.EmailLink] = (5, 600),
            [RateLimitPolicies.PasskeyCeremony] = (20, 60),
            [RateLimitPolicies.Authenticated] = (60, 60),
            [RateLimitPolicies.AvatarUpload] = (10, 600),
            [RateLimitPolicies.Public] = (120, 60),
        };

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            foreach (var (policy, defaults) in Defaults)
            {
                var permitLimit = configuration.GetValue($"RateLimiting:{policy}:PermitLimit", defaults.PermitLimit);
                var windowSeconds = configuration.GetValue($"RateLimiting:{policy}:WindowSeconds", defaults.WindowSeconds);

                options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                    GetPartitionKey(context.User, context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueLimit = 0,
                    }));
            }
        });

        return services;
    }

    /// <summary>Authenticated callers are limited per account, anonymous callers per client IP.</summary>
    internal static string GetPartitionKey(ClaimsPrincipal? user, IPAddress? remoteIp)
    {
        var userId = user?.Identity?.IsAuthenticated == true
            ? user.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        return string.IsNullOrEmpty(userId)
            ? $"ip:{remoteIp?.ToString() ?? "unknown"}"
            : $"user:{userId}";
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        var policy = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        IdentityMetrics.RecordRateLimited(policy ?? "unknown");

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            httpContext.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
        }

        await TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many requests. Try again later.",
            type: RateLimitCode,
            extensions: new Dictionary<string, object?> { ["code"] = RateLimitCode })
            .ExecuteAsync(httpContext);
    }
}