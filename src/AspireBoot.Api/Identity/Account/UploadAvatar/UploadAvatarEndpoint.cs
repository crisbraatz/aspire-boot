using System.Security.Claims;

using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Infrastructure.Images;
using AspireBoot.Core.Infrastructure.Storage;
using AspireBoot.Core.Messaging.Identity;
using AspireBoot.Core.Persistence;
using AspireBoot.Core.Shared;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Account.UploadAvatar;

public sealed record UploadAvatarResponse(string AvatarUrl);

/// <summary>
/// Replaces the signed-in user's avatar. The upload is decoded and re-encoded server side (see ADR 0004), then:
/// A) a delayed orphan check is committed in its own transaction, so an object left by a crash is always cleaned;
/// B) the object is written to private storage;
/// C) the profile pointer is switched in a fresh scope per attempt (xmin concurrency), publishing the superseded
/// object for deletion. <c>SaveChangesAndFlushMessagesAsync</c> commits, so there is no explicit commit.
/// </summary>
internal sealed partial class UploadAvatarEndpoint
{
    /// <summary>Largest accepted upload. Anything bigger gets 413 avatar.tooLarge.</summary>
    internal const long MaxFileBytes = 5_242_880;

    /// <summary>Whole-request ceiling: the file limit plus multipart overhead. Enforced by the RequestSizeLimiting middleware.</summary>
    internal const long MaxRequestBytes = 6 * 1024 * 1024;

    private const int MaxAttempts = 2;

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/avatar", Handle)
            .WithName("UploadAvatar")
            .WithSummary("Replaces the signed-in user's avatar with an uploaded JPEG, PNG or WebP (up to 5 MB), stored as a 256x256 WebP.")
            .Accepts<IFormFile>("multipart/form-data")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.AvatarUpload)
            // Overrides the 32 KB identity group limit; enforced by the RequestSizeLimiting middleware.
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes);

    internal static async Task<Results<Ok<UploadAvatarResponse>, UnauthorizedHttpResult, ProblemHttpResult>> Handle(
        HttpRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        AvatarImageProcessor processor,
        AvatarProcessingLimiter limiter,
        IAvatarStorage storage,
        IDbContextOutbox<AppDbContext> orphanOutbox,
        IServiceScopeFactory scopes,
        IOptions<AvatarOptions> options,
        ILogger<UploadAvatarEndpoint> logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(users.GetUserId(principal), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        // The form is read here, not bound as an IFormFile parameter: parameter binding runs before the antiforgery
        // filter and would answer 415 to a body of the wrong type before the token is checked.
        var file = await ReadFileAsync(request, cancellationToken);
        if (file is null)
        {
            return Problem("avatar.missing", "Send the image in the 'file' field.", StatusCodes.Status400BadRequest);
        }

        if (file.Length > MaxFileBytes)
        {
            IdentityMetrics.RecordAvatarUpload("rejected_size");
            return Problem("avatar.tooLarge", "The image is larger than 5 MB.", StatusCodes.Status413PayloadTooLarge);
        }

        if (!await limiter.WaitAsync(cancellationToken))
        {
            ProcessingBusy(logger);
            return Problem("avatar.busy", "Too many images are being processed. Try again shortly.", StatusCodes.Status503ServiceUnavailable);
        }

        Result<byte[]> processed;
        try
        {
            // Decoding is CPU-bound: keep it off the request thread.
            processed = await Task.Run(
                () =>
                {
                    using var stream = file.OpenReadStream();
                    return processor.Process(stream);
                },
                cancellationToken);
        }
        finally
        {
            limiter.Release();
        }

        if (!processed.IsSuccess)
        {
            return Rejected(processed.Failure!, logger);
        }

        var objectId = Guid.CreateVersion7();
        var key = AvatarReference.KeyFor(userId, objectId);

        // Step A: schedule the orphan check first and commit it on its own, so it exists even if we crash after storing.
        await using (var transaction = await orphanOutbox.DbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await orphanOutbox.ScheduleAsync(new AvatarObjectOrphanCheck(userId, key), options.Value.OrphanCheckDelay);
            await orphanOutbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        }

        // Step B: store the object. The profile is untouched until step C.
        try
        {
            await storage.PutAsync(key, processed.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StorageFailed(logger, exception, userId);
            IdentityMetrics.RecordAvatarUpload("storage_error");
            return Problem("avatar.storageUnavailable", "The image could not be stored. Try again shortly.", StatusCodes.Status503ServiceUnavailable);
        }

        // Step C: switch the profile pointer. A new scope per attempt discards the envelopes of a failed attempt.
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<AppDbContext>>();
            var db = outbox.DbContext;

            var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
            if (user is null)
            {
                await DeleteQuietlyAsync(storage, key, userId, logger);
                return TypedResults.Unauthorized();
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var superseded = user.ChangeAvatar(objectId);
            if (superseded is not null)
            {
                await outbox.PublishAsync(new AvatarObjectSuperseded(userId, superseded.Key));
            }

            try
            {
                // Saves the profile, persists the messages and commits the transaction.
                await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                ConcurrentChange(logger, userId, attempt);
                continue;
            }

            IdentityMetrics.RecordAvatarUpload("stored");
            AvatarStored(logger, userId, user.AvatarVersion);
            return TypedResults.Ok(new UploadAvatarResponse($"/api/identity/account/avatar/{user.AvatarVersion}"));
        }

        // Still conflicting after the retries: remove the new object (the orphan check covers a failed removal).
        await DeleteQuietlyAsync(storage, key, userId, logger);
        IdentityMetrics.RecordAvatarUpload("conflict");
        return Problem("avatar.busy", "The profile changed at the same time. Try again.", StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IFormFile?> ReadFileAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return null;
        }

        try
        {
            var form = await request.ReadFormAsync(cancellationToken);
            return form.Files.GetFile("file");
        }
        catch (InvalidDataException)
        {
            // Malformed multipart body.
            return null;
        }
    }

    private static ProblemHttpResult Rejected(Failure failure, ILogger logger)
    {
        if (failure == AvatarImageFailures.UnsupportedType)
        {
            IdentityMetrics.RecordAvatarUpload("rejected_type");
            return Problem(failure.Code, failure.Description, StatusCodes.Status415UnsupportedMediaType);
        }

        if (failure == AvatarImageFailures.DimensionsTooLarge)
        {
            IdentityMetrics.RecordAvatarUpload("rejected_dimensions");
            return Problem(failure.Code, failure.Description, StatusCodes.Status400BadRequest);
        }

        IdentityMetrics.RecordAvatarUpload("corrupt");
        ImageRejected(logger, failure.Code);
        return Problem(AvatarImageFailures.Corrupt.Code, AvatarImageFailures.Corrupt.Description, StatusCodes.Status400BadRequest);
    }

    private static async Task DeleteQuietlyAsync(IAvatarStorage storage, string key, Guid userId, ILogger logger)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            CleanupFailed(logger, exception, userId);
        }
    }

    private static ProblemHttpResult Problem(string code, string title, int statusCode) =>
        TypedResults.Problem(
            title: title,
            statusCode: statusCode,
            type: code,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Avatar version {Version} stored for user {UserId}.")]
    private static partial void AvatarStored(ILogger logger, Guid userId, int version);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Avatar upload rejected: {Code}.")]
    private static partial void ImageRejected(ILogger logger, string code);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Avatar processing is saturated; request refused with 503.")]
    private static partial void ProcessingBusy(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Avatar storage failed for user {UserId}; the profile was not changed.")]
    private static partial void StorageFailed(ILogger logger, Exception exception, Guid userId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Concurrent profile change for user {UserId} on avatar attempt {Attempt}.")]
    private static partial void ConcurrentChange(ILogger logger, Guid userId, int attempt);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Could not delete the unused avatar object for user {UserId}; the orphan check will.")]
    private static partial void CleanupFailed(ILogger logger, Exception exception, Guid userId);
}