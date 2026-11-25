using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Auth.BeginRegistration;

public sealed record BeginRegistrationRequest(string? UserId, string? Token, string? Purpose);

/// <summary>
/// Starts the first passkey registration from an email link and returns the WebAuthn creation options.
/// Any invalid, used or expired link gets the same generic 400.
/// </summary>
internal sealed partial class BeginRegistrationEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/registration/options", Handle)
            .WithName("BeginPasskeyRegistration")
            .WithSummary("Returns passkey creation options for a valid confirmation or recovery link.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PasskeyCeremony);

    internal static async Task<Results<ContentHttpResult, ProblemHttpResult>> Handle(
        BeginRegistrationRequest request,
        UserManager<User> users,
        SignInManager<User> signIn,
        ILogger<BeginRegistrationEndpoint> logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userId)
            || !IdentityLinkTokens.TryDecode(request.Token, out var token)
            || request.Purpose is null)
        {
            LinkRejected(logger);
            return IdentityLinkTokens.InvalidLink();
        }

        var user = await IdentityLinkTokens.FindUserForPurposeAsync(users, userId, request.Purpose, token);
        if (user is null)
        {
            LinkRejected(logger);
            return IdentityLinkTokens.InvalidLink();
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Passkey registration options issued for user {UserId}.")]
    private static partial void OptionsIssued(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Passkey registration refused: the link is invalid or expired.")]
    private static partial void LinkRejected(ILogger logger);
}