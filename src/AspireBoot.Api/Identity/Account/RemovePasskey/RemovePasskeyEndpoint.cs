using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Account.RemovePasskey;

/// <summary>
/// Removes one passkey, unless it is the account's last one. The account row is locked (SELECT ... FOR UPDATE)
/// before the count, so concurrent removals on the same account run one after the other and the last passkey
/// can never be removed. The security stamp change ends the other sessions and the current one is re-issued.
/// </summary>
internal sealed partial class RemovePasskeyEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete("/passkeys/{id}", Handle)
            .WithName("RemovePasskey")
            .WithSummary("Removes one of the signed-in user's passkeys. The last passkey cannot be removed.")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> Handle(
        string id,
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        ISecurityStampCache stamps,
        IDbContextOutbox<AppDbContext> outbox,
        ILogger<RemovePasskeyEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!PasskeyIds.TryDecode(id, out var credentialId))
        {
            return PasskeyProblems.NotFound();
        }

        var db = outbox.DbContext;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serializes removals for this account: the second request waits here and counts after the first commits.
        // Only the id is selected: SELECT * would drop the xmin system column that the users row version needs.
        _ = await db.Database
            .SqlQuery<Guid>($"SELECT \"id\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"id\" = {user.Id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

        var passkey = await users.GetPasskeyAsync(user, credentialId);
        if (passkey is null)
        {
            return PasskeyProblems.NotFound();
        }

        var passkeys = await users.GetPasskeysAsync(user);
        var canRemove = user.EnsureCanRemovePasskey(passkeys.Count);
        if (!canRemove.IsSuccess)
        {
            LastPasskeyRefused(logger, user.Id);
            return PasskeyProblems.LastPasskey();
        }

        var removed = await users.RemovePasskeyAsync(user, credentialId);
        if (!removed.Succeeded)
        {
            return PasskeyProblems.NotFound();
        }

        // Invalidates other sessions; the current session is re-issued below, after the commit.
        await users.UpdateSecurityStampAsync(user);

        await outbox.PublishAsync(new PasskeyRemovedNotification(
            user.Id, user.Email!, user.DisplayName.Value, user.PreferredCulture.Value, passkey.Name ?? string.Empty));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        // Overwrites the cached stamp after the commit, so the other sessions are rejected on their next request.
        await stamps.StampChangedAsync(user.Id);
        await signIn.RefreshSignInAsync(user);

        IdentityMetrics.RecordPasskeyRemoved();
        PasskeyRemoved(logger, user.Id);
        return TypedResults.NoContent();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Passkey removed for user {UserId}.")]
    private static partial void PasskeyRemoved(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Passkey removal refused for user {UserId}: it is the last passkey.")]
    private static partial void LastPasskeyRefused(ILogger logger, Guid userId);
}