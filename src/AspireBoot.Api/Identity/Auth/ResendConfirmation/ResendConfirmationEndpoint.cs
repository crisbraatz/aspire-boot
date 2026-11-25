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

namespace AspireBoot.Api.Identity.Auth.ResendConfirmation;

public sealed record ResendConfirmationRequest(string? Email);

/// <summary>
/// Sends a new confirmation email to an unconfirmed account. Always answers 202 with an empty body.
/// A cooldown per email (hashed in the cache key) stops repeated sends.
/// </summary>
internal sealed partial class ResendConfirmationEndpoint
{
    private const string CooldownKeyPrefix = "identity:resend-confirmation:";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/confirmation/resend", Handle)
            .WithName("ResendConfirmation")
            .WithSummary("Sends a new confirmation email to an unconfirmed account. Answers 202 in every case.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.EmailLink);

    internal static async Task<Results<Accepted, ValidationProblem>> Handle(
        ResendConfirmationRequest request,
        IDbContextOutbox<AppDbContext> outbox,
        UserManager<User> users,
        IDistributedCache cache,
        IOptions<IdentityFlowOptions> flowOptions,
        IConfiguration configuration,
        ILogger<ResendConfirmationEndpoint> logger,
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
        if (user is null || user.EmailConfirmed)
        {
            NoPendingConfirmation(logger);
            return TypedResults.Accepted((string?)null);
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = IdentityLinks.Confirmation(configuration, user.Id, token);

        await outbox.PublishAsync(new ConfirmationEmailRequested(
            user.Id, user.Email!, user.DisplayName.Value, user.PreferredCulture.Value, link));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        ConfirmationQueued(logger, user.Id);
        return TypedResults.Accepted((string?)null);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Confirmation email queued for user {UserId}.")]
    private static partial void ConfirmationQueued(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Resend ignored: the email cooldown is active.")]
    private static partial void CooldownActive(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Resend answered without changes: no unconfirmed account for the email.")]
    private static partial void NoPendingConfirmation(ILogger logger);
}