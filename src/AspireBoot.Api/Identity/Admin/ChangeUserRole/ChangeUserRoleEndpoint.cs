using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;
using AspireBoot.Core.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Admin.ChangeUserRole;

public sealed record ChangeUserRoleRequest(string? Role);

/// <summary>
/// Moves a user to the Admin or User role (single role per user). The Admin role row is locked (SELECT ... FOR UPDATE)
/// before the Admin count, so concurrent changes run one after the other and the last Admin can never be removed.
/// The security stamp change plus the cache overwrite after the commit end the target's sessions at once.
/// </summary>
internal sealed partial class ChangeUserRoleEndpoint
{
    public const string NotFoundCode = "identity.userNotFound";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/{id:guid}/role", Handle)
            .WithName("ChangeUserRole")
            .WithSummary("Changes the role of a user (Admin or User). Self-demotion and removing the last Admin are refused.");

    internal static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> Handle(
        Guid id,
        ChangeUserRoleRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        ISecurityStampCache stamps,
        IUserSessionCache sessions,
        TimeProvider clock,
        IDbContextOutbox<AppDbContext> outbox,
        ILogger<ChangeUserRoleEndpoint> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Guid.TryParse(users.GetUserId(principal), out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        if (!Roles.IsKnown(request.Role))
        {
            return Problem(RoleAssignment.InvalidRole, StatusCodes.Status400BadRequest);
        }

        var newRole = request.Role!;
        var db = outbox.DbContext;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serializes every role change: the next request waits here and counts the Admins after this one commits.
        // Only the id is selected: SELECT * would drop the xmin system column that the users row version needs.
        _ = await db.Database
            .SqlQuery<Guid>($"SELECT \"id\" AS \"Value\" FROM \"AspNetRoles\" WHERE \"id\" = {Roles.AdminId} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

        var target = await users.FindByIdAsync(id.ToString("D"));
        if (target is null)
        {
            return Problem(new Failure(NotFoundCode, "The user was not found."), StatusCodes.Status404NotFound);
        }

        var adminCount = await db.UserRoles.CountAsync(assignment => assignment.RoleId == Roles.AdminId, cancellationToken);
        var currentRole = await users.IsInRoleAsync(target, Roles.Admin) ? Roles.Admin : Roles.User;

        var allowed = RoleAssignment.EnsureCanChangeRole(actorId, target.Id, currentRole, newRole, adminCount);
        if (!allowed.IsSuccess)
        {
            var failure = allowed.Failure!;
            Refused(logger, actorId, target.Id, failure.Code);
            return Problem(failure, StatusCodes.Status409Conflict);
        }

        var assigned = await users.GetRolesAsync(target);
        if (assigned.Count == 1 && string.Equals(assigned[0], newRole, StringComparison.Ordinal))
        {
            return TypedResults.NoContent();
        }

        // Single role per user: remove whatever else is assigned (normalizes legacy double roles), then add the new one.
        var others = assigned.Where(role => !string.Equals(role, newRole, StringComparison.Ordinal)).ToList();
        if (others.Count > 0)
        {
            var removed = await users.RemoveFromRolesAsync(target, others);
            if (!removed.Succeeded)
            {
                return OperationFailed(logger, actorId, target.Id, "remove-roles", removed);
            }
        }

        if (!assigned.Contains(newRole, StringComparer.Ordinal))
        {
            var added = await users.AddToRoleAsync(target, newRole);
            if (!added.Succeeded)
            {
                return OperationFailed(logger, actorId, target.Id, "add-role", added);
            }
        }

        var stamped = await users.UpdateSecurityStampAsync(target);
        if (!stamped.Succeeded)
        {
            return OperationFailed(logger, actorId, target.Id, "update-stamp", stamped);
        }

        // Tracked, so the revocations are audited and saved by the same commit as the role change.
        var revoked = await UserSessionRevocation.RevokeAsync(db, target.Id, exceptSessionId: null, clock.GetUtcNow(), cancellationToken);

        await outbox.PublishAsync(new RoleChangedNotification(
            target.Id, target.Email!, target.DisplayName.Value, target.PreferredCulture.Value, newRole));
        // Commits the explicit transaction as well (no CommitAsync afterwards).
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        // Overwrites the cached stamp after the commit, so the target's sessions are rejected on their next request.
        await stamps.StampChangedAsync(target.Id);
        await sessions.SessionsChangedAsync(revoked);
        if (revoked.Count > 0)
        {
            IdentityMetrics.RecordSessionsEnded("stamp", revoked.Count);
        }

        IdentityMetrics.RecordRoleChanged(newRole);
        RoleChanged(logger, actorId, target.Id, newRole);
        return TypedResults.NoContent();
    }

    // Returning before the commit disposes the transaction, which rolls back every earlier write of the request.
    private static ProblemHttpResult OperationFailed(ILogger logger, Guid actorId, Guid targetId, string operation, IdentityResult result)
    {
        OperationFailedLog(logger, actorId, targetId, operation, IdentityOperationFailure.Codes(result));
        return IdentityOperationFailure.Problem();
    }

    private static ProblemHttpResult Problem(Failure failure, int statusCode) =>
        TypedResults.Problem(
            title: failure.Description,
            statusCode: statusCode,
            type: failure.Code,
            extensions: new Dictionary<string, object?> { ["code"] = failure.Code });

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "User {ActorId} changed the role of user {TargetId} to {Role}.")]
    private static partial void RoleChanged(ILogger logger, Guid actorId, Guid targetId, string role);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Role change by user {ActorId} on user {TargetId} failed at {Operation}: {Codes}.")]
    private static partial void OperationFailedLog(ILogger logger, Guid actorId, Guid targetId, string operation, string codes);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Role change by user {ActorId} on user {TargetId} refused: {Code}.")]
    private static partial void Refused(ILogger logger, Guid actorId, Guid targetId, string code);
}