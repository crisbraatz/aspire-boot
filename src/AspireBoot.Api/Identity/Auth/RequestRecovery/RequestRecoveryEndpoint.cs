using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Auth.RequestRecovery;

public sealed record RequestRecoveryRequest(string? Email);

/// <summary>
/// Sends a passkey recovery link to a confirmed account. Always answers 202 with an empty body, whether the email is
/// unknown, unconfirmed or confirmed, so the response never reveals which accounts exist. A per-email cooldown applies.
/// </summary>
internal sealed partial class RequestRecoveryEndpoint
{
    private const string CooldownKeyPrefix = "identity:recovery:";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/recovery", Handle)
            .WithName("RequestRecovery")
            .WithSummary("Sends a passkey recovery link to a confirmed account. Answers 202 in every case.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.EmailLink);

    internal static async Task<Results<Accepted, ValidationProblem>> Handle(
        RequestRecoveryRequest request,
        IDbContextOutbox<AppDbContext> outbox,
        UserManager<User> users,
        IDistributedCache cache,
        IOptions<IdentityFlowOptions> flowOptions,
        IConfiguration configuration,
        ILogger<RequestRecoveryEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        if (!IdentityEmail.IsValid(email))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["email"] = ["Email is invalid."],
            });
        }

        if (!await EmailCooldown.TryStartAsync(cache, CooldownKeyPrefix, email, flowOptions.Value.EmailCooldownSeconds, cancellationToken))
        {
            CooldownActive(logger);
            return TypedResults.Accepted((string?)null);
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.EmailConfirmed)
        {
            NoRecoverableAccount(logger);
            return TypedResults.Accepted((string?)null);
        }

        var token = await users.GenerateUserTokenAsync(user, PasskeyRecoveryTokenProvider<User>.ProviderName, IdentityLinkTokens.PasskeyRecoveryPurpose);
        var link = IdentityLinks.Recovery(configuration, user.Id, token);

        await outbox.PublishAsync(new RecoveryEmailRequested(
            user.Id, user.Email!, user.DisplayName.Value, user.PreferredCulture.Value, link));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        IdentityMetrics.RecordRecoveryRequested();
        RecoveryQueued(logger, user.Id);
        return TypedResults.Accepted((string?)null);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Recovery email queued for user {UserId}.")]
    private static partial void RecoveryQueued(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Recovery ignored: the email cooldown is active.")]
    private static partial void CooldownActive(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Recovery answered without changes: no confirmed account for the email.")]
    private static partial void NoRecoverableAccount(ILogger logger);
}