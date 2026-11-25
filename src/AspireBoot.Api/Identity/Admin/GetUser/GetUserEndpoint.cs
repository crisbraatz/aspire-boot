using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Admin.GetUser;

public sealed record AdminUserDetail(
    Guid Id,
    string DisplayName,
    string Email,
    string Role,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt,
    string? AvatarUrl,
    int PasskeyCount,
    string Culture);

/// <summary>
/// Returns one user account for administrators. Only the fields an administrator needs are projected: no security
/// stamp, tokens, passkey data or avatar storage keys. The passkey count is a bounded subquery, not a loaded list.
/// </summary>
internal static class GetUserEndpoint
{
    public const string NotFoundCode = "identity.userNotFound";

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{id:guid}", Handle)
            .WithName("GetUser")
            .WithSummary("Returns one user account for administrators, with passkey count and culture.");

    internal static async Task<Results<Ok<AdminUserDetail>, ProblemHttpResult>> Handle(
        Guid id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var adminUserIds = db.UserRoles.Where(assignment => assignment.RoleId == Roles.AdminId).Select(assignment => assignment.UserId);

        var row = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.Email,
                user.EmailConfirmed,
                user.CreatedAt,
                user.PreferredCulture,
                AvatarKey = EF.Property<string?>(user, "avatarKey"),
                user.AvatarVersion,
                IsAdmin = adminUserIds.Contains(user.Id),
                PasskeyCount = db.Set<IdentityUserPasskey<Guid>>().Count(passkey => passkey.UserId == user.Id),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return NotFound();
        }

        return TypedResults.Ok(new AdminUserDetail(
            row.Id,
            row.DisplayName.Value,
            row.Email ?? string.Empty,
            row.IsAdmin ? Roles.Admin : Roles.User,
            row.EmailConfirmed,
            row.CreatedAt,
            AvatarStreaming.AdminAvatarUrl(row.Id, row.AvatarKey, row.AvatarVersion),
            row.PasskeyCount,
            row.PreferredCulture.Value));
    }

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(
            title: "The user was not found.",
            statusCode: StatusCodes.Status404NotFound,
            type: NotFoundCode,
            extensions: new Dictionary<string, object?> { ["code"] = NotFoundCode });
}