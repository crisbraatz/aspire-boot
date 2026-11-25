namespace AspireBoot.Api.Shared;

/// <summary>Named authorization policies. Endpoints reference these constants, never raw strings.</summary>
internal static class AuthorizationPolicies
{
    /// <summary>The signed-in user must hold the built-in Admin role.</summary>
    public const string Admin = "admin";
}