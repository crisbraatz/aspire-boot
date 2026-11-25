using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AspireBoot.Api.Shared;

/// <summary>
/// The antiforgery request cookie is HttpOnly, so the SPA cannot read it. This endpoint issues the request
/// token in a readable <c>XSRF-TOKEN</c> cookie that Angular echoes in the <c>X-XSRF-TOKEN</c> header.
/// </summary>
internal static class GetAntiforgeryTokenEndpoint
{
    public const string CookieName = "XSRF-TOKEN";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/token", Handle)
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issues the antiforgery token in a readable XSRF-TOKEN cookie.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static NoContent Handle(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);

        context.Response.Cookies.Append(CookieName, tokens.RequestToken!, new CookieOptions
        {
            Secure = true,
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });

        return TypedResults.NoContent();
    }
}