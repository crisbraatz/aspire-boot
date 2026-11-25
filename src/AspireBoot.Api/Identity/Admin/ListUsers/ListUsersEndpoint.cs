using AspireBoot.Api.Identity.Shared;
using AspireBoot.Api.Shared;
using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AspireBoot.Api.Identity.Admin.ListUsers;

public sealed record AdminUserSummary(Guid Id, string DisplayName, string Email, string Role, bool EmailConfirmed, DateTimeOffset CreatedAt, string? AvatarUrl);

public sealed record AdminUserPage(IReadOnlyList<AdminUserSummary> Items, int Page, int PageSize, int Total);

/// <summary>
/// Pages through user accounts for administrators, newest first. Every bound is checked before the query runs, so a
/// request can never ask for an unbounded list. Search is a case-insensitive contains match on display name and email,
/// with LIKE wildcards escaped so user input is always a literal.
/// </summary>
internal static class ListUsersEndpoint
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet(string.Empty, Handle)
            .WithName("ListUsers")
            .WithSummary("Lists user accounts (newest first), with optional search and role filter. Administrators only.");

    internal static async Task<Results<Ok<AdminUserPage>, ValidationProblem>> Handle(
        AppDbContext db,
        string? search,
        string? role,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (page < 1)
        {
            errors["page"] = ["Page must be at least 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        var term = search?.Trim();
        if (term?.Length > MaxSearchLength)
        {
            errors["search"] = [$"Search must have at most {MaxSearchLength} characters."];
        }

        if (role is not null && !Roles.IsKnown(role))
        {
            errors["role"] = ["Role is not known."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // One built-in role per user: the Admin membership decides the role, the User role is everyone else.
        var adminUserIds = db.UserRoles.Where(assignment => assignment.RoleId == Roles.AdminId).Select(assignment => assignment.UserId);

        IQueryable<User> users = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{EscapeLike(term)}%";
            // The display name is a value object, so EF cannot run ILIKE on it. The ids are matched in SQL instead,
            // with the pattern as a parameter; the subquery is bounded by the same filter as the email match.
            var nameMatches = db.Database.SqlQuery<Guid>($"SELECT \"id\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"display_name\" ILIKE {pattern}");
            users = users.Where(user => nameMatches.Contains(user.Id) || EF.Functions.ILike(user.Email!, pattern));
        }

        users = role switch
        {
            Roles.Admin => users.Where(user => adminUserIds.Contains(user.Id)),
            Roles.User => users.Where(user => !adminUserIds.Contains(user.Id)),
            _ => users,
        };

        var total = await users.CountAsync(cancellationToken);

        var rows = await users
            .OrderByDescending(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.Email,
                user.EmailConfirmed,
                user.CreatedAt,
                AvatarKey = EF.Property<string?>(user, "avatarKey"),
                user.AvatarVersion,
                IsAdmin = adminUserIds.Contains(user.Id),
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<AdminUserSummary> items = rows
            .Select(row => new AdminUserSummary(
                row.Id,
                row.DisplayName.Value,
                row.Email ?? string.Empty,
                row.IsAdmin ? Roles.Admin : Roles.User,
                row.EmailConfirmed,
                row.CreatedAt,
                AvatarStreaming.AdminAvatarUrl(row.Id, row.AvatarKey, row.AvatarVersion)))
            .ToList();

        return TypedResults.Ok(new AdminUserPage(items, page, pageSize, total));
    }

    /// <summary>Escapes the LIKE metacharacters (and the escape character itself) so the search text matches literally.</summary>
    internal static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}