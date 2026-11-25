using System.Security.Claims;

using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Account.BeginAddPasskey;

/// <summary>Returns the WebAuthn creation options for an additional passkey on the signed-in account.</summary>
internal sealed partial class BeginAddPasskeyEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/passkeys/options", Handle)
            .WithName("BeginAddPasskey")
            .WithSummary("Returns passkey creation options for the signed-in user.")
            .RequireRateLimiting(RateLimitPolicies.PasskeyCeremony);

    internal static async Task<Results<ContentHttpResult, UnauthorizedHttpResult>> Handle(
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        ILogger<BeginAddPasskeyEndpoint> logger)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString("D"),
            Name = user.Email!,
            DisplayName = user.DisplayName.Value,
        });

        OptionsIssued(logger, user.Id);
        return TypedResults.Content(options, "application/json");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Passkey creation options issued for user {UserId}.")]
    private static partial void OptionsIssued(ILogger logger, Guid userId);
}