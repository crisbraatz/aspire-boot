using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Auth.SignOut;

/// <summary>Ends the current session (its row and the cookie). Requires an authenticated caller (fallback policy).</summary>
internal static class SignOutEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/signout", Handle)
            .WithName("SignOut")
            .WithSummary("Ends the current session.")
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<NoContent> Handle(
        ClaimsPrincipal principal,
        AppDbContext db,
        SignInManager<User> signIn,
        IUserSessionCache sessions,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(principal.FindFirstValue(SessionClaims.SessionId), out var sessionId)
            && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            // Filtering by the caller's id: a session id from a forged claim of another account is never revoked here.
            var session = await db.UserSessions.FirstOrDefaultAsync(row => row.Id == sessionId && row.UserId == userId, cancellationToken);
            if (session is not null)
            {
                var now = clock.GetUtcNow();
                var wasActive = session.IsActive(now);
                session.Revoke(now);
                await db.SaveChangesAsync(cancellationToken);
                await sessions.SessionsChangedAsync([sessionId]);
                if (wasActive)
                {
                    IdentityMetrics.RecordSessionsEnded("signout", 1);
                }
            }
        }

        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }
}