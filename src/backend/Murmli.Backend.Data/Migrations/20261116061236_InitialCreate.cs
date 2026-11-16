using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Murmli.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "products");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "deleted_users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    zitadel_user_id = table.Column<string>(type: "text", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deleted_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_import_runs",
                schema: "products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    peak_memory_bytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_import_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "products",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    brand = table.Column<string>(type: "text", nullable: true),
                    generic_name = table.Column<string>(type: "text", nullable: true),
                    is_per_hundred_milliliters = table.Column<bool>(type: "boolean", nullable: false),
                    energy_kilojoules = table.Column<double>(type: "double precision", nullable: true),
                    energy_kilojoules_is_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    energy_kilocalories = table.Column<double>(type: "double precision", nullable: true),
                    energy_kilocalories_is_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    fat = table.Column<double>(type: "double precision", nullable: true),
                    fat_is_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    saturated_fat = table.Column<double>(type: "double precision", nullable: true),
                    saturated_fat_is_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    carbohydrates = table.Column<double>(type: "double precision", nullable: true),
                    carbohydrates_is_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    sugars = table.Column<double>(type: "double precision", nullable: true),
                    sugars_is_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    added_sugars = table.Column<double>(type: "double precision", nullable: true),
                    added_sugars_is_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    proteins = table.Column<double>(type: "double precision", nullable: true),
                    proteins_is_estimated = table.Column<bool>(type: "boolean", nullable: false),
                    popularity_key = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zitadel_user_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deleted_users_zitadel_user_id",
                schema: "identity",
                table: "deleted_users",
                column: "zitadel_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_brand_trigram",
                schema: "products",
                table: "products",
                column: "brand")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_products_name_trigram",
                schema: "products",
                table: "products",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_users_zitadel_user_id",
                schema: "identity",
                table: "users",
                column: "zitadel_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deleted_users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "product_import_runs",
                schema: "products");

            migrationBuilder.DropTable(
                name: "products",
                schema: "products");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
