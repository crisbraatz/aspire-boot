namespace AspireBoot.Api.Identity.Shared;

/// <summary>Claim types the API adds to the application cookie principal.</summary>
internal static class SessionClaims
{
    /// <summary>The id of the <c>UserSession</c> row the cookie belongs to (ADR 0007).</summary>
    public const string SessionId = "aspireboot:sid";
}