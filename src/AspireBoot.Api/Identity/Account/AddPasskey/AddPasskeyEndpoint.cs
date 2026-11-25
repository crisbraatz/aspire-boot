using System.Security.Claims;
using System.Text.Json;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Account.AddPasskey;

public sealed record AddPasskeyRequest(JsonElement? Credential, string? Name);

/// <summary>
/// Verifies the new passkey, saves it and queues the "passkey added" email. The save and the outbox message commit
/// together: the outbox flush commits the transaction.
/// </summary>
internal sealed partial class AddPasskeyEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/passkeys", Handle)
            .WithName("AddPasskey")
            .WithSummary("Adds a passkey to the signed-in account and notifies the owner by email.")
            .RequireRateLimiting(RateLimitPolicies.PasskeyCeremony);

    internal static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> Handle(
        AddPasskeyRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        SignInManager<User> signIn,
        IDbContextOutbox<AppDbContext> outbox,
        ILogger<AddPasskeyEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!PasskeyIds.TryNormalizeName(request.Name, out var name))
        {
            errors["name"] = [$"Name must have between 1 and {PasskeyIds.MaxNameLength} characters."];
        }

        string? credentialJson = null;
        if (request.Credential is not { ValueKind: JsonValueKind.Object } credential)
        {
            errors["credential"] = ["Credential must be a JSON object."];
        }
        else
        {
            credentialJson = credential.GetRawText();
            if (credentialJson.Length > PasskeyIds.MaxCredentialLength)
            {
                errors["credential"] = [$"Credential must have at most {PasskeyIds.MaxCredentialLength} characters."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Verify the credential before any write: a failed ceremony leaves the account untouched.
        var attestation = await signIn.PerformPasskeyAttestationAsync(credentialJson!);
        if (!attestation.Succeeded || attestation.Passkey is null || attestation.UserEntity?.Id != user.Id.ToString("D"))
        {
            PasskeyRejected(logger, user.Id);
            return IdentityLinkTokens.PasskeyFailed();
        }

        var passkey = attestation.Passkey;
        passkey.Name = name;

        await using var transaction = await outbox.DbContext.Database.BeginTransactionAsync(cancellationToken);

        var saved = await users.AddOrUpdatePasskeyAsync(user, passkey);
        if (!saved.Succeeded)
        {
            PasskeyRejected(logger, user.Id);
            return IdentityLinkTokens.PasskeyFailed();
        }

        await outbox.PublishAsync(new PasskeyAddedNotification(
            user.Id, user.Email!, user.DisplayName.Value, user.PreferredCulture.Value, name));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        IdentityMetrics.RecordPasskeyAdded();
        PasskeyAdded(logger, user.Id);
        return TypedResults.NoContent();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Passkey added for user {UserId}.")]
    private static partial void PasskeyAdded(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Passkey addition refused for user {UserId}: the credential could not be verified.")]
    private static partial void PasskeyRejected(ILogger logger, Guid userId);
}