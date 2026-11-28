using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Murmli.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shopping");

            migrationBuilder.CreateTable(
                name: "items",
                schema: "shopping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    unit = table.Column<string>(type: "text", nullable: true),
                    amount_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    category_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    @checked = table.Column<bool>(name: "checked", type: "boolean", nullable: false),
                    checked_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_arrived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_member_entries_changed_by_member_id",
                        column: x => x.changed_by_member_id,
                        principalSchema: "sync",
                        principalTable: "member_entries",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_items_member_entries_created_by_member_id",
                        column: x => x.created_by_member_id,
                        principalSchema: "sync",
                        principalTable: "member_entries",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_items_scopes_scope_id",
                        column: x => x.scope_id,
                        principalSchema: "sync",
                        principalTable: "scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lists",
                schema: "shopping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lists", x => x.id);
                    table.CheckConstraint("ck_lists_scope_id", "scope_id = id");
                    table.ForeignKey(
                        name: "fk_lists_scopes_scope_id",
                        column: x => x.scope_id,
                        principalSchema: "sync",
                        principalTable: "scopes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_items_changed_by_member_id",
                schema: "shopping",
                table: "items",
                column: "changed_by_member_id",
                filter: "changed_by_member_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_checked_arrived_at",
                schema: "shopping",
                table: "items",
                column: "checked_arrived_at",
                filter: "checked_arrived_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_created_by_member_id",
                schema: "shopping",
                table: "items",
                column: "created_by_member_id",
                filter: "created_by_member_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_scope_id_version",
                schema: "shopping",
                table: "items",
                columns: new[] { "scope_id", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_lists_scope_id_version",
                schema: "shopping",
                table: "lists",
                columns: new[] { "scope_id", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "items",
                schema: "shopping");

            migrationBuilder.DropTable(
                name: "lists",
                schema: "shopping");
        }
    }
}
