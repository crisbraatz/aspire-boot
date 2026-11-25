using System.Security.Claims;

using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Account.UpdateProfile;

public sealed record UpdateProfileRequest(string? DisplayName);

/// <summary>Saves the signed-in user's profile. The display name follows the DisplayName rules in the domain.</summary>
internal sealed partial class UpdateProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/profile", Handle)
            .WithName("UpdateProfile")
            .WithSummary("Saves the signed-in user's display name (2 to 100 characters).")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> Handle(
        UpdateProfileRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        ILogger<UpdateProfileEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var displayName = DisplayName.Create(request.DisplayName);
        if (!displayName.IsSuccess)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["displayName"] = [displayName.Failure!.Code],
            });
        }

        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        user.Rename(displayName.Value);

        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["displayName"] = ["identity.displayNameNotSaved"],
            });
        }

        ProfileUpdated(logger, user.Id);
        return TypedResults.NoContent();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Display name updated for user {UserId}.")]
    private static partial void ProfileUpdated(ILogger logger, Guid userId);
}