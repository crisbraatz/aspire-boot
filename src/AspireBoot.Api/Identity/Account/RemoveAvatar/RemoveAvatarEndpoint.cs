using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Account.RemoveAvatar;

/// <summary>
/// Removes the signed-in user's avatar. Idempotent: it always answers 204. The superseded object is published through
/// the outbox in the same transaction as the profile change, and the Worker deletes it.
/// <c>SaveChangesAndFlushMessagesAsync</c> commits, so there is no explicit commit.
/// </summary>
internal sealed partial class RemoveAvatarEndpoint
{
    private const int MaxAttempts = 2;

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete("/avatar", Handle)
            .WithName("RemoveAvatar")
            .WithSummary("Removes the signed-in user's avatar. Always returns 204, even when there is none.")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> Handle(
        ClaimsPrincipal principal,
        UserManager<User> users,
        IServiceScopeFactory scopes,
        ILogger<RemoveAvatarEndpoint> logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(users.GetUserId(principal), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        // A new scope per attempt discards the envelopes of a failed attempt.
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<AppDbContext>>();
            var db = outbox.DbContext;

            var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var superseded = user.RemoveAvatar();
            if (superseded is not null)
            {
                await outbox.PublishAsync(new AvatarObjectSuperseded(userId, superseded.Key));
            }

            try
            {
                // Saves the profile, persists the messages and commits the transaction.
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                ConcurrentChange(logger, userId, attempt);
                continue;
            }

            if (superseded is not null)
            {
                IdentityMetrics.RecordAvatarRemoval();
                AvatarRemoved(logger, userId);
            }

            return TypedResults.NoContent();
        }

        return TypedResults.Problem(
            title: "The profile changed at the same time. Try again.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: "avatar.busy",
            extensions: new Dictionary<string, object?> { ["code"] = "avatar.busy" });
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Avatar removed for user {UserId}.")]
    private static partial void AvatarRemoved(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Concurrent profile change for user {UserId} on avatar removal attempt {Attempt}.")]
    private static partial void ConcurrentChange(ILogger logger, Guid userId, int attempt);
}