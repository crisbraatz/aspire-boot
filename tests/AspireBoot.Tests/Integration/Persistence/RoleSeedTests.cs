using AspireBoot.Core.Identity.Domain;
using AspireBoot.Core.Persistence;
using AspireBoot.Tests.Integration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace AspireBoot.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
[Collection(SharedInfrastructure.Name)]
public sealed class RoleSeedTests(InfrastructureFixture infrastructure)
{
    private const string MigrationBeforeSeed = "20261125204131_AddAuditEntries";

    [Fact]
    public async Task SeededRoles_HaveFixedIds_AndSurviveTheTestReset()
    {
        var ct = TestContext.Current.CancellationToken;
        await infrastructure.ResetDatabaseAsync();

        var roles = await ReadRolesAsync(infrastructure.PostgresConnectionString, ct);

        Assert.Equal(
            [("ADMIN", Roles.AdminId, "Admin"), ("USER", Roles.UserId, "User")],
            roles);
    }

    [Fact]
    public async Task SeedRoles_OnADatabaseWithARandomIdUserRole_RemapsAssignmentsToTheFixedId()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = "seedroles_" + Guid.NewGuid().ToString("N");
        var legacyUserRole = Guid.NewGuid();
        var legacyAdminRole = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var admin = new NpgsqlConnectionStringBuilder(infrastructure.PostgresConnectionString);
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await ExecuteAsync(connection, $"CREATE DATABASE \"{database}\"", ct);
        }

        var target = new NpgsqlConnectionStringBuilder(infrastructure.PostgresConnectionString) { Database = database }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            options.UseAspireBootNpgsql(target).UseStandaloneIdentitySchema();
            await using var context = new AppDbContext(options.Options);
            await context.GetService<IMigrator>().MigrateAsync(MigrationBeforeSeed, ct);

            await using (var connection = new NpgsqlConnection(target))
            {
                await connection.OpenAsync(ct);
                await ExecuteAsync(connection, $"""
                    INSERT INTO "AspNetRoles" (id, name, normalized_name, concurrency_stamp) VALUES
                        ('{legacyUserRole}', 'User', 'USER', 'legacy'),
                        ('{legacyAdminRole}', 'Admin', 'ADMIN', 'legacy');
                    INSERT INTO "AspNetUsers" (id, display_name, created_at, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
                    VALUES ('{userA}', 'A', now(), true, false, false, false, 0), ('{userB}', 'B', now(), true, false, false, false, 0);
                    INSERT INTO "AspNetUserRoles" (user_id, role_id) VALUES ('{userA}', '{legacyUserRole}'), ('{userB}', '{legacyAdminRole}');
                    INSERT INTO "AspNetRoleClaims" (role_id, claim_type, claim_value) VALUES ('{legacyUserRole}', 'k', 'v');
                    """, ct);
            }

            await context.Database.MigrateAsync(ct);

            Assert.Equal(
                [("ADMIN", Roles.AdminId, "Admin"), ("USER", Roles.UserId, "User")],
                await ReadRolesAsync(target, ct));

            await using var verify = new NpgsqlConnection(target);
            await verify.OpenAsync(ct);
            Assert.Equal(Roles.UserId, await ScalarAsync(verify, $"""SELECT role_id FROM "AspNetUserRoles" WHERE user_id = '{userA}'""", ct));
            Assert.Equal(Roles.AdminId, await ScalarAsync(verify, $"""SELECT role_id FROM "AspNetUserRoles" WHERE user_id = '{userB}'""", ct));
            Assert.Equal(Roles.UserId, await ScalarAsync(verify, """SELECT role_id FROM "AspNetRoleClaims" WHERE claim_type = 'k'""", ct));
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", ct);
        }
    }

    private static async Task<(string Normalized, Guid Id, string Name)[]> ReadRolesAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""SELECT normalized_name, id, name FROM "AspNetRoles" ORDER BY normalized_name""", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<(string, Guid, string)>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add((reader.GetString(0), reader.GetGuid(1), reader.GetString(2)));
        }

        return [.. rows];
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(ct);
    }
}