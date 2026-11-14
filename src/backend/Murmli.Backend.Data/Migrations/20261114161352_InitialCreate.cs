using Microsoft.EntityFrameworkCore.Migrations;

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
                name: "products");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "products",
                schema: "products",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
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
                    proteins_is_estimated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.code);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_name_trigram",
                schema: "products",
                table: "products",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products",
                schema: "products");
        }
    }
}
