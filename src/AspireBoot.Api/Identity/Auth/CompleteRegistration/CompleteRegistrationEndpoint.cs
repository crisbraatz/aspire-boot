using System.Text.Json;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspireBoot.Api.Identity.Auth.CompleteRegistration;

public sealed record CompleteRegistrationRequest(
    string? UserId,
    string? Token,
    string? Purpose,
    JsonElement? Credential,
    string? Name);

/// <summary>
/// Saves the first passkey and, for a confirmation link, confirms the email. The passkey, the confirmation and the
/// security stamp change (which makes the link single-use and ends other sessions) commit in one transaction.
/// </summary>
internal sealed partial class CompleteRegistrationEndpoint
{
    private const int MaxNameLength = 50;

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/registration", Handle)
            .WithName("CompletePasskeyRegistration")
            .WithSummary("Registers the first passkey from a valid link, confirms the email and signs the user in.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PasskeyCeremony);

    internal static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> Handle(
        CompleteRegistrationRequest request,
        AppDbContext db,
        UserManager<User> users,
        SignInManager<User> signIn,
        SessionSignIn sessionSignIn,
        HttpContext httpContext,
        ISecurityStampCache stamps,
        IUserSessionCache sessions,
        TimeProvider clock,
        IAuditActorOverride auditActor,
        IOptions<BootstrapOptions> bootstrap,
        ILogger<CompleteRegistrationEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.Purpose is not (IdentityLinkTokens.ConfirmPurpose or IdentityLinkTokens.RecoverPurpose))
        {
            errors["purpose"] = [$"Purpose must be '{IdentityLinkTokens.ConfirmPurpose}' or '{IdentityLinkTokens.RecoverPurpose}'."];
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > MaxNameLength)
        {
            errors["name"] = [$"Name must have between 1 and {MaxNameLength} characters."];
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

        // Verify the credential before any write: a failed ceremony leaves the account untouched.
        var attestation = await signIn.PerformPasskeyAttestationAsync(credentialJson!);
        if (!attestation.Succeeded || attestation.Passkey is null || attestation.UserEntity?.Id != user.Id.ToString("D"))
        {
            PasskeyRejected(logger);
            return IdentityLinkTokens.PasskeyFailed();
        }

        // The caller is anonymous: the audit rows of this flow name the owner of the link as the actor.
        using var actingAs = auditActor.ActAs(user.Id);

        var passkey = attestation.Passkey;
        passkey.Name = name;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var saved = await users.AddOrUpdatePasskeyAsync(user, passkey);
        if (!saved.Succeeded)
        {
            PasskeyRejected(logger);
            return IdentityLinkTokens.PasskeyFailed();
        }

        if (request.Purpose == IdentityLinkTokens.ConfirmPurpose)
        {
            var confirmed = await users.ConfirmEmailAsync(user, token);
            if (!confirmed.Succeeded)
            {
                LinkRejected(logger);
                return IdentityLinkTokens.InvalidLink();
            }

            // Confirm purpose only: a recovery never grants Admin. Roles are seeded, so no lazy creation.
            var alreadyAdmin = await users.IsInRoleAsync(user, Roles.Admin);
            if (RoleAssignment.ShouldBootstrapAdmin(bootstrap.Value.AdminEmail, user.Email ?? string.Empty, user.EmailConfirmed, alreadyAdmin))
            {
                var granted = await users.AddToRoleAsync(user, Roles.Admin);
                if (!granted.Succeeded)
                {
                    return OperationFailed(logger, user.Id, "grant-admin", granted);
                }

                var demoted = await users.RemoveFromRoleAsync(user, Roles.User);
                if (!demoted.Succeeded)
                {
                    return OperationFailed(logger, user.Id, "remove-user-role", demoted);
                }

                IdentityMetrics.RecordAdminBootstrapped();
                AdminBootstrapped(logger, user.Id);
            }
        }

        // Invalidates the link token (single use) and any other session of the account.
        var stamped = await users.UpdateSecurityStampAsync(user);
        if (!stamped.Succeeded)
        {
            return OperationFailed(logger, user.Id, "update-stamp", stamped);
        }

        // Every earlier session ends and the new row is added in the same commit. The cookie is issued only after the commit:
        // if that fails, the row is an orphan nobody holds the id of, and it expires naturally.
        var revoked = await UserSessionRevocation.RevokeAsync(db, user.Id, exceptSessionId: null, clock.GetUtcNow(), cancellationToken);
        var session = sessionSignIn.CreateSession(user, isPersistent: false, httpContext);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Sequential and never throwing: one failing never skips the other, and either alone rejects the other sessions.
        await stamps.StampChangedAsync(user.Id);
        await sessions.SessionsChangedAsync(revoked);
        if (revoked.Count > 0)
        {
            IdentityMetrics.RecordSessionsEnded("stamp", revoked.Count);
        }

        // The account is already confirmed and the link consumed: a cookie failure must not look like a retryable error.
        var cookieIssued = true;
        try
        {
            await sessionSignIn.IssueCookieAsync(user, isPersistent: false, session.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            cookieIssued = false;
            CookieIssueFailed(logger, user.Id, exception.GetType().Name);
        }

        IdentityMetrics.RecordPasskeyAdded();
        if (request.Purpose == IdentityLinkTokens.ConfirmPurpose)
        {
            IdentityMetrics.RecordEmailConfirmation();
        }
        else
        {
            IdentityMetrics.RecordRecoveryCompleted();
        }

        RegistrationCompleted(logger, user.Id);
        if (!cookieIssued)
        {
            return IdentityLinkTokens.SignInRequired();
        }

        return TypedResults.NoContent();
    }

    // Returning before the commit disposes the transaction, which rolls back the passkey, the confirmation and the roles.
    private static ProblemHttpResult OperationFailed(ILogger logger, Guid userId, string operation, IdentityResult result)
    {
        OperationFailedLog(logger, userId, operation, IdentityOperationFailure.Codes(result));
        return IdentityOperationFailure.Problem();
    }

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Passkey registration for user {UserId} failed at {Operation}: {Codes}.")]
    private static partial void OperationFailedLog(ILogger logger, Guid userId, string operation, string codes);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Registration of user {UserId} was committed but the sign-in cookie failed ({ErrorType}); the user must sign in.")]
    private static partial void CookieIssueFailed(ILogger logger, Guid userId, string errorType);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "First passkey registered for user {UserId}.")]
    private static partial void RegistrationCompleted(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Passkey registration refused: the link is invalid or expired.")]
    private static partial void LinkRejected(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Admin granted to user {UserId} by the bootstrap email.")]
    private static partial void AdminBootstrapped(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Passkey registration refused: the credential could not be verified.")]
    private static partial void PasskeyRejected(ILogger logger);
}