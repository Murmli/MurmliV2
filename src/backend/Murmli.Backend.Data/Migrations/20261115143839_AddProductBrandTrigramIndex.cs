using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Murmli.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductBrandTrigramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_products_brand_trigram",
                schema: "products",
                table: "products",
                column: "brand")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_brand_trigram",
                schema: "products",
                table: "products");
        }
    }
}
