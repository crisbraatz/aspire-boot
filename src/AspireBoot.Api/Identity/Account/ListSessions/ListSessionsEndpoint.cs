using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Account.ListSessions;

public sealed record SessionSummary(
    Guid Id,
    string Browser,
    string Os,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    string IpMasked,
    bool Current);

/// <summary>Lists the signed-in user's active sessions, most recently seen first. Bounded to 50 entries; never cached.</summary>
internal static class ListSessionsEndpoint
{
    public const int MaxSessions = 50;

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/sessions", Handle)
            .WithName("ListSessions")
            .WithSummary("Lists the signed-in user's active sessions (browser, system, masked address, last activity).")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<Ok<IReadOnlyList<SessionSummary>>, UnauthorizedHttpResult>> Handle(
        ClaimsPrincipal principal,
        HttpContext httpContext,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        _ = Guid.TryParse(principal.FindFirstValue(SessionClaims.SessionId), out var currentId);
        var now = clock.GetUtcNow();

        var rows = await db.UserSessions
            .AsNoTracking()
            .Where(session => session.UserId == userId && session.RevokedAt == null && session.ExpiresAt > now)
            .OrderByDescending(session => session.LastSeenAt)
            .Take(MaxSessions)
            .Select(session => new { session.Id, session.UserAgent, session.CreatedAt, session.LastSeenAt, session.IpMasked })
            .ToListAsync(cancellationToken);

        IReadOnlyList<SessionSummary> response = rows
            .Select(row =>
            {
                var agent = UserAgentSummary.FromStored(row.UserAgent);
                return new SessionSummary(row.Id, agent.Browser, agent.Os, row.CreatedAt, row.LastSeenAt, row.IpMasked, row.Id == currentId);
            })
            .ToList();

        httpContext.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(response);
    }
}