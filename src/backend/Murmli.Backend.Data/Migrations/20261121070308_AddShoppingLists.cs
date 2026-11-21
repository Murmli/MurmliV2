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
                name: "lists",
                schema: "shopping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "items",
                schema: "shopping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<double>(type: "double precision", nullable: true),
                    unit = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<string>(type: "text", nullable: true),
                    @checked = table.Column<bool>(name: "checked", type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_lists_list_id",
                        column: x => x.list_id,
                        principalSchema: "shopping",
                        principalTable: "lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "list_members",
                schema: "shopping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_list_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_list_members_lists_list_id",
                        column: x => x.list_id,
                        principalSchema: "shopping",
                        principalTable: "lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_list_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_items_list_id",
                schema: "shopping",
                table: "items",
                column: "list_id");

            migrationBuilder.CreateIndex(
                name: "ix_list_members_list_id_user_id",
                schema: "shopping",
                table: "list_members",
                columns: new[] { "list_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_list_members_user_id",
                schema: "shopping",
                table: "list_members",
                column: "user_id");

            migrationBuilder.Sql("CREATE PUBLICATION powersync FOR TABLE identity.users, shopping.lists, shopping.list_members, shopping.items;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PUBLICATION IF EXISTS powersync;");

            migrationBuilder.DropTable(
                name: "items",
                schema: "shopping");

            migrationBuilder.DropTable(
                name: "list_members",
                schema: "shopping");

            migrationBuilder.DropTable(
                name: "lists",
                schema: "shopping");
        }
    }
}
