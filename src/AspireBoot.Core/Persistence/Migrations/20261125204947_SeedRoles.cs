using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AspireBoot.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedRoles : Migration
    {
        private const string AdminId = "0198a000-0000-7000-8000-000000000001";
        private const string UserId = "0198a000-0000-7000-8000-000000000002";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Safe on databases that already hold a User or Admin role with a random id (created lazily by older code):
            // 1) park such roles under a unique legacy name, so the unique index on normalized_name stays free;
            // 2) insert the fixed-id roles (idempotent);
            // 3) move every reference (user roles, role claims) to the fixed id, then drop the legacy rows.
            migrationBuilder.Sql($"""
                UPDATE "AspNetRoles"
                SET name = 'legacy-' || normalized_name || '-' || id::text,
                    normalized_name = 'LEGACY-' || normalized_name || '-' || upper(id::text)
                WHERE normalized_name IN ('ADMIN', 'USER') AND id NOT IN ('{AdminId}', '{UserId}');
                """);

            migrationBuilder.Sql($"""
                INSERT INTO "AspNetRoles" (id, name, normalized_name, concurrency_stamp) VALUES
                    ('{AdminId}', 'Admin', 'ADMIN', '5d1f4a0e-0000-4000-8000-000000000001'),
                    ('{UserId}', 'User', 'USER', '5d1f4a0e-0000-4000-8000-000000000002')
                ON CONFLICT DO NOTHING;
                """);

            foreach (var (name, fixedId) in new[] { ("ADMIN", AdminId), ("USER", UserId) })
            {
                // A user already holding the fixed role keeps a single assignment.
                migrationBuilder.Sql($"""
                    DELETE FROM "AspNetUserRoles" ur
                    USING "AspNetRoles" r
                    WHERE ur.role_id = r.id AND r.normalized_name LIKE 'LEGACY-{name}-%'
                      AND EXISTS (SELECT 1 FROM "AspNetUserRoles" x WHERE x.user_id = ur.user_id AND x.role_id = '{fixedId}');
                    """);
                migrationBuilder.Sql($"""
                    UPDATE "AspNetUserRoles" ur SET role_id = '{fixedId}'
                    FROM "AspNetRoles" r
                    WHERE ur.role_id = r.id AND r.normalized_name LIKE 'LEGACY-{name}-%';
                    """);
                migrationBuilder.Sql($"""
                    UPDATE "AspNetRoleClaims" rc SET role_id = '{fixedId}'
                    FROM "AspNetRoles" r
                    WHERE rc.role_id = r.id AND r.normalized_name LIKE 'LEGACY-{name}-%';
                    """);
            }

            migrationBuilder.Sql("""DELETE FROM "AspNetRoles" WHERE normalized_name LIKE 'LEGACY-%';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Roles still referenced by users are kept: removing them would orphan assignments.
            migrationBuilder.Sql("""
                DELETE FROM "AspNetRoles" r
                WHERE r.id IN ('0198a000-0000-7000-8000-000000000001', '0198a000-0000-7000-8000-000000000002')
                  AND NOT EXISTS (SELECT 1 FROM "AspNetUserRoles" ur WHERE ur.role_id = r.id);
                """);
        }
    }
}