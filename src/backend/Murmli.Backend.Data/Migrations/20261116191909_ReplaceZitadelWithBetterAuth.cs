using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Murmli.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceZitadelWithBetterAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows carry Zitadel IDs, which never match a Better Auth user
            migrationBuilder.Sql("DELETE FROM identity.deleted_users;");
            migrationBuilder.Sql("DELETE FROM identity.users;");

            migrationBuilder.RenameColumn(
                name: "zitadel_user_id",
                schema: "identity",
                table: "users",
                newName: "auth_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_users_zitadel_user_id",
                schema: "identity",
                table: "users",
                newName: "ix_users_auth_user_id");

            migrationBuilder.RenameColumn(
                name: "zitadel_user_id",
                schema: "identity",
                table: "deleted_users",
                newName: "auth_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_deleted_users_zitadel_user_id",
                schema: "identity",
                table: "deleted_users",
                newName: "ix_deleted_users_auth_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "auth_user_id",
                schema: "identity",
                table: "users",
                newName: "zitadel_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_users_auth_user_id",
                schema: "identity",
                table: "users",
                newName: "ix_users_zitadel_user_id");

            migrationBuilder.RenameColumn(
                name: "auth_user_id",
                schema: "identity",
                table: "deleted_users",
                newName: "zitadel_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_deleted_users_auth_user_id",
                schema: "identity",
                table: "deleted_users",
                newName: "ix_deleted_users_zitadel_user_id");
        }
    }
}
