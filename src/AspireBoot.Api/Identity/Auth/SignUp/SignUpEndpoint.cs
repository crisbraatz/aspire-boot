using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Auth.SignUp;

public sealed record SignUpRequest(string? DisplayName, string? Email, string? Culture);

/// <summary>
/// Starts a passwordless account. Always answers 202 with an empty body, whether or not the email is new,
/// so the response does not reveal which emails have accounts.
/// </summary>
internal sealed partial class SignUpEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/auth/signup", Handle)
            .WithName("SignUp")
            .WithSummary("Starts an account by email. Answers 202 for new and existing emails alike.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Signup);

    internal static async Task<Results<Accepted, ValidationProblem>> Handle(
        SignUpRequest request,
        IDbContextOutbox<AppDbContext> outbox,
        UserManager<User> users,
        RoleManager<Role> roles,
        IConfiguration configuration,
        TimeProvider clock,
        ILogger<SignUpEndpoint> logger,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var displayName = DisplayName.Create(request.DisplayName);
        if (!displayName.IsSuccess)
        {
            errors["displayName"] = [displayName.Failure!.Description];
        }

        var culture = UiCulture.Create(request.Culture);
        if (!culture.IsSuccess)
        {
            errors["culture"] = [culture.Failure!.Description];
        }

        var email = request.Email?.Trim() ?? string.Empty;
        if (!IdentityEmail.IsValid(email))
        {
            errors["email"] = ["Email is invalid."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            AlreadyRegistered(logger);
            return TypedResults.Accepted((string?)null);
        }

        var user = User.Create(email, displayName.Value, culture.Value, clock);

        // The account, its role and the outbox message commit together, or not at all: the outbox flush commits this transaction.
        await using var transaction = await outbox.DbContext.Database.BeginTransactionAsync(cancellationToken);

        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            // Lost a race with a concurrent signup for the same email. Answer the same way.
            AlreadyRegistered(logger);
            return TypedResults.Accepted((string?)null);
        }

        if (!await roles.RoleExistsAsync(Roles.User))
        {
            await roles.CreateAsync(Role.Create(Roles.User));
        }

        await users.AddToRoleAsync(user, Roles.User);

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = IdentityLinks.Confirmation(configuration, user.Id, token);

        await outbox.PublishAsync(new ConfirmationEmailRequested(
            user.Id, email, displayName.Value.Value, culture.Value.Value, link));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        IdentityMetrics.RecordSignUp();
        AccountCreated(logger, user.Id);
        return TypedResults.Accepted((string?)null);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Account created; confirmation email queued for user {UserId}.")]
    private static partial void AccountCreated(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Sign-up answered without changes: the email is already registered.")]
    private static partial void AlreadyRegistered(ILogger logger);
}