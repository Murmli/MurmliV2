using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Murmli.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveProductTablesToProductsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "products");

            migrationBuilder.RenameTable(
                name: "products",
                newName: "products",
                newSchema: "products");

            migrationBuilder.RenameTable(
                name: "product_import_runs",
                newName: "product_import_runs",
                newSchema: "products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "products",
                schema: "products",
                newName: "products");

            migrationBuilder.RenameTable(
                name: "product_import_runs",
                schema: "products",
                newName: "product_import_runs");
        }
    }
}
