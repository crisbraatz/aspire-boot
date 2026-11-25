using System.Security.Claims;

using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AspireBoot.Api.Identity.Account.UpdatePreferences;

public sealed record UpdatePreferencesRequest(string? Culture);

/// <summary>Saves the signed-in user's interface culture. Later identity emails are rendered in it.</summary>
internal sealed partial class UpdatePreferencesEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/preferences", Handle)
            .WithName("UpdatePreferences")
            .WithSummary("Saves the signed-in user's interface culture (pt-BR, en-US or es-ES).")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Authenticated);

    internal static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> Handle(
        UpdatePreferencesRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        ILogger<UpdatePreferencesEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var culture = UiCulture.Create(request.Culture);
        if (!culture.IsSuccess)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["culture"] = [culture.Failure!.Description],
            });
        }

        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        user.ChangePreferredCulture(culture.Value);

        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["culture"] = ["The culture could not be saved."],
            });
        }

        PreferencesUpdated(logger, user.Id);
        return TypedResults.NoContent();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Interface culture updated for user {UserId}.")]
    private static partial void PreferencesUpdated(ILogger logger, Guid userId);
}