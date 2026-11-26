using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Account.EndSession;

/// <summary>
/// Ends one of the signed-in user's other sessions. Another account's id and an unknown id are both 404 (no IDOR, no
/// existence oracle); the current session is 409 (use sign-out); an already ended one is an idempotent 204.
/// </summary>
internal sealed partial class EndSessionEndpoint
{
    public const string NotFoundCode = "identity.sessionNotFound";
    public const string CurrentSessionCode = "identity.currentSession";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete("/sessions/{id:guid}", Handle)
            .WithName("EndSession")
            .WithSummary("Ends one of the signed-in user's other sessions.")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> Handle(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        IUserSessionCache sessions,
        TimeProvider clock,
        ILogger<EndSessionEndpoint> logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (Guid.TryParse(principal.FindFirstValue(SessionClaims.SessionId), out var currentId) && currentId == id)
        {
            return Problem("This is the current session: sign out instead.", CurrentSessionCode, StatusCodes.Status409Conflict);
        }

        // Filtered by the caller's id: a session of another account is indistinguishable from an unknown one.
        var session = await db.UserSessions.FirstOrDefaultAsync(row => row.Id == id && row.UserId == userId, cancellationToken);
        if (session is null)
        {
            return Problem("The session was not found.", NotFoundCode, StatusCodes.Status404NotFound);
        }

        var now = clock.GetUtcNow();
        if (!session.IsActive(now))
        {
            return TypedResults.NoContent();
        }

        session.Revoke(now);
        await db.SaveChangesAsync(cancellationToken);
        await sessions.SessionsChangedAsync([id]);

        IdentityMetrics.RecordSessionsEnded("user", 1);
        SessionEnded(logger, userId, id);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult Problem(string title, string code, int statusCode) =>
        TypedResults.Problem(
            title: title,
            statusCode: statusCode,
            type: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "User {UserId} ended session {SessionId}.")]
    private static partial void SessionEnded(ILogger logger, Guid userId, Guid sessionId);
}