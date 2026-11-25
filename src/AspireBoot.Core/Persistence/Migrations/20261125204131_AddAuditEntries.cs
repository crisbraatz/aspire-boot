using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AspireBoot.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    table_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entity_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at",
                table: "audit_entries",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_key_occurred_at",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_key", "occurred_at" });

            // Append-only: no UPDATE, DELETE or TRUNCATE, enforced by the database. The only bypass is
            // session_replication_role = replica, which needs superuser (maintenance and the test reset).
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_entries_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_entries is append-only';
                END;
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER audit_entries_no_update_delete BEFORE UPDATE OR DELETE ON audit_entries
                FOR EACH ROW EXECUTE FUNCTION audit_entries_append_only();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER audit_entries_no_truncate BEFORE TRUNCATE ON audit_entries
                FOR EACH STATEMENT EXECUTE FUNCTION audit_entries_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER audit_entries_no_truncate ON audit_entries;");
            migrationBuilder.Sql("DROP TRIGGER audit_entries_no_update_delete ON audit_entries;");
            migrationBuilder.Sql("DROP FUNCTION audit_entries_append_only();");

            migrationBuilder.DropTable(
                name: "audit_entries");
        }
    }
}