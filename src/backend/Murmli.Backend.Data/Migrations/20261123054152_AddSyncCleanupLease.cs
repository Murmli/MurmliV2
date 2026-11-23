using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Murmli.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncCleanupLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cleanup_lease",
                schema: "sync",
                columns: table => new
                {
                    singleton = table.Column<bool>(type: "boolean", nullable: false),
                    holder = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cleanup_lease", x => x.singleton);
                    table.CheckConstraint("ck_cleanup_lease_singleton", "singleton");
                });

            migrationBuilder.InsertData(
                schema: "sync",
                table: "cleanup_lease",
                columns: new[] { "singleton", "expires_at", "holder" },
                values: new object[] { true, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cleanup_lease",
                schema: "sync");
        }
    }
}
