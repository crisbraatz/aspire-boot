using System.Security.Claims;

using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AspireBoot.Api.Identity.Shared;

/// <summary>
/// The only way to sign an account in (ADR 0007): every cookie carries the id of a <c>UserSession</c> row in the
/// <see cref="SessionClaims.SessionId"/> claim. <c>SignInManager.RefreshSignInAsync</c> must not be used, it drops that claim.
/// </summary>
internal class SessionSignIn(
    SignInManager<User> signIn,
    AppDbContext db,
    TimeProvider clock,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions)
{
    /// <summary>Creates the row, saves it and issues the cookie.</summary>
    public async Task SignInAsync(User user, bool isPersistent, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var session = CreateSession(user, isPersistent, httpContext);
        await db.SaveChangesAsync(cancellationToken);
        await IssueCookieAsync(user, isPersistent, session.Id);
    }

    /// <summary>Adds the tracked row without saving, so a caller can commit it in its own transaction.</summary>
    public UserSession CreateSession(User user, bool isPersistent, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(httpContext);

        var session = UserSession.Start(
            user.Id,
            clock.GetUtcNow(),
            cookieOptions.Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan,
            isPersistent,
            UserAgentSummary.Parse(httpContext.Request.Headers.UserAgent.ToString()),
            MaskedIpAddress.From(httpContext.Connection.RemoteIpAddress));
        db.UserSessions.Add(session);
        return session;
    }

    /// <summary>Only issues the cookie for an already committed session row. Virtual only as the test seam for a cookie failure.</summary>
    public virtual Task IssueCookieAsync(User user, bool isPersistent, Guid sessionId) =>
        signIn.SignInWithClaimsAsync(user, isPersistent, [new Claim(SessionClaims.SessionId, sessionId.ToString("D"))]);

    /// <summary>Re-issues the cookie after a stamp change, keeping the session id and the persistence setting. False (and signed out) when the cookie is not this user's.</summary>
    public async Task<bool> RefreshAsync(User user, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(httpContext);

        var result = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = result.Principal;
        var sid = principal?.FindFirstValue(SessionClaims.SessionId);
        if (!result.Succeeded || principal is null || result.Properties is null || sid is null
            || principal.FindFirstValue(ClaimTypes.NameIdentifier) != user.Id.ToString())
        {
            await signIn.SignOutAsync();
            return false;
        }

        var claims = new List<Claim>();
        foreach (var type in new[] { "amr", ClaimTypes.AuthenticationMethod })
        {
            var value = principal.FindFirstValue(type);
            if (value is not null)
            {
                claims.Add(new Claim(type, value));
            }
        }

        claims.Add(new Claim(SessionClaims.SessionId, sid));
        await signIn.SignInWithClaimsAsync(user, result.Properties, claims);
        return true;
    }
}