using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Auth.BeginSignIn;

/// <summary>
/// Returns passkey request options for a discoverable sign-in. No account is needed (or revealed) at this step.
/// </summary>
internal sealed partial class BeginSignInEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/signin/options", Handle)
            .WithName("BeginPasskeySignIn")
            .WithSummary("Returns passkey request options for a discoverable sign-in.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PasskeyCeremony);

    internal static async Task<ContentHttpResult> Handle(
        SignInManager<User> signIn,
        ILogger<BeginSignInEndpoint> logger)
    {
        var options = await signIn.MakePasskeyRequestOptionsAsync(null);

        OptionsIssued(logger);
        return TypedResults.Content(options, "application/json");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Passkey sign-in options issued.")]
    private static partial void OptionsIssued(ILogger logger);
}