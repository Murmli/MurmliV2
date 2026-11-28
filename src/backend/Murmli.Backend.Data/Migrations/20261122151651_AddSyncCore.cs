using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Murmli.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.CreateTable(
                name: "database_identity",
                schema: "sync",
                columns: table => new
                {
                    singleton = table.Column<bool>(type: "boolean", nullable: false),
                    value = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_database_identity", x => x.singleton);
                    table.CheckConstraint("ck_database_identity_singleton", "singleton");
                });

            migrationBuilder.CreateTable(
                name: "processed_commands",
                schema: "sync",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_type = table.Column<string>(type: "text", nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    cause = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_commands", x => new { x.account_id, x.command_id });
                    table.ForeignKey(
                        name: "fk_processed_commands_users_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scopes",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    owner_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    epoch = table.Column<int>(type: "integer", nullable: false),
                    tombstone_floor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scopes", x => x.id);
                    table.ForeignKey(
                        name: "fk_scopes_users_owner_account_id",
                        column: x => x.owner_account_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tombstones",
                schema: "sync",
                columns: table => new
                {
                    data_type = table.Column<string>(type: "text", nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tombstones", x => new { x.data_type, x.record_id });
                    table.ForeignKey(
                        name: "fk_tombstones_scopes_scope_id",
                        column: x => x.scope_id,
                        principalSchema: "sync",
                        principalTable: "scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "sync",
                table: "database_identity",
                columns: new[] { "singleton", "value" },
                values: new object[] { true, new Guid("3643d4ef-a971-42c0-be19-cc2c3180dc55") });

            migrationBuilder.CreateIndex(
                name: "ix_processed_commands_retain_from",
                schema: "sync",
                table: "processed_commands",
                column: "retain_from");

            migrationBuilder.CreateIndex(
                name: "ix_processed_commands_root_deletes",
                schema: "sync",
                table: "processed_commands",
                columns: new[] { "account_id", "scope_id" },
                filter: "kind = 'delete' AND status = 'applied' AND record_id = scope_id");

            migrationBuilder.CreateIndex(
                name: "ix_scopes_owner_account_id_kind",
                schema: "sync",
                table: "scopes",
                columns: new[] { "owner_account_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ux_scopes_owner_account_id_personal",
                schema: "sync",
                table: "scopes",
                column: "owner_account_id",
                unique: true,
                filter: "kind = 'personal'");

            migrationBuilder.CreateIndex(
                name: "ix_tombstones_deleted_at",
                schema: "sync",
                table: "tombstones",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_tombstones_scope_id_version",
                schema: "sync",
                table: "tombstones",
                columns: new[] { "scope_id", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "database_identity",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "processed_commands",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "tombstones",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "scopes",
                schema: "sync");
        }
    }
}
