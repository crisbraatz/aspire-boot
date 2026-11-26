using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Account.EndOtherSessions;

public sealed record EndOtherSessionsResponse(int Ended);

/// <summary>Ends every active session of the signed-in user except the current one.</summary>
internal sealed partial class EndOtherSessionsEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/sessions/end-others", Handle)
            .WithName("EndOtherSessions")
            .WithSummary("Ends every other session of the signed-in user and returns how many were ended.")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<Ok<EndOtherSessionsResponse>, UnauthorizedHttpResult>> Handle(
        ClaimsPrincipal principal,
        AppDbContext db,
        IUserSessionCache sessions,
        TimeProvider clock,
        ILogger<EndOtherSessionsEndpoint> logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(SessionClaims.SessionId), out var currentId))
        {
            return TypedResults.Unauthorized();
        }

        var ended = await UserSessionRevocation.RevokeAsync(db, userId, currentId, clock.GetUtcNow(), cancellationToken);
        if (ended.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await sessions.SessionsChangedAsync(ended);
            IdentityMetrics.RecordSessionsEnded("others", ended.Count);
        }

        OthersEnded(logger, userId, ended.Count);
        return TypedResults.Ok(new EndOtherSessionsResponse(ended.Count));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "User {UserId} ended {Count} other sessions.")]
    private static partial void OthersEnded(ILogger logger, Guid userId, int count);
}