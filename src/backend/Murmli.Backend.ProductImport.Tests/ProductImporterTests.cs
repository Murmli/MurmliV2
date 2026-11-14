using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.ProductImport.Tests;

public class ProductImporterTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    public static TheoryData<byte[]> EmptyExports => new() { Array.Empty<byte>(), CompressedExport.Create([]) };

    public static TheoryData<string, string, Product> ProductChanges =>
        new()
        {
            {
                CreateExportLine("4000000000011", "Apfel", 0.2),
                CreateExportLine("4000000000011", "Apfel", 0.2, fatSource: "estimate"),
                new Product
                {
                    Code = "4000000000011",
                    Name = "Apfel",
                    IsPerHundredMilliliters = false,
                    Fat = 0.2,
                    FatIsEstimated = true,
                }
            },
            {
                CreateExportLine("4000000000011", "Apfel", 0.2),
                CreateExportLine("4000000000011", "Apfel", 0.2, per: "100ml"),
                new Product
                {
                    Code = "4000000000011",
                    Name = "Apfel",
                    IsPerHundredMilliliters = true,
                    Fat = 0.2,
                }
            },
            {
                CreateExportLine("4000000000011", "Apfel", 0.2),
                CreateExportLine("4000000000011", "Apfel", 0.2, proteins: 0.3),
                new Product
                {
                    Code = "4000000000011",
                    Name = "Apfel",
                    IsPerHundredMilliliters = false,
                    Fat = 0.2,
                    Proteins = 0.3,
                }
            },
            {
                CreateExportLine("4000000000011", "Apfel", 0.2, proteins: 0.3),
                CreateExportLine("4000000000011", "Apfel", 0.2),
                new Product
                {
                    Code = "4000000000011",
                    Name = "Apfel",
                    IsPerHundredMilliliters = false,
                    Fat = 0.2,
                }
            },
            { CreateExportLine("4000000000001", "Haferflocken", 7), OatFlakes.ExportLine, OatFlakes.ExpectedProduct },
        };

    [Fact]
    public async Task ImportAsync_ChangedExport_InsertsUpdatesAndDeletesProducts()
    {
        const string noNutritionLine = """
            {"code":"4000000000016","countries_tags":["en:germany"],"product_name_de":"Gewürzmischung"}
            """;
        const string frenchLine = """
            {"code":"3000000000002","countries_tags":["en:france"],"product_name":"Baguette"}
            """;
        const string germanyOnlyInOriginsLine = """
            {"code":"3000000000003","countries_tags":["en:france"],"origins_tags":["en:germany"],"product_name":"Fromage"}
            """;
        const string unnamedLine = """
            {"code":"4000000000098","countries_tags":["en:germany"]}
            """;

        var importedProducts = await ImportAndReadProductsAsync(
            CompressedExport.Create(
                [
                    OatFlakes.ExportLine,
                    CreateExportLine("4000000000012", "Apfelmus", 0.1),
                    CreateExportLine("4000000000013", "Butter", 83),
                    CreateExportLine("4000000000015", "Apfelschorle", 0, per: "100ml"),
                    noNutritionLine,
                    frenchLine,
                    germanyOnlyInOriginsLine,
                    unnamedLine,
                ]
            )
        );

        Assert.Equal(
            ["4000000000001", "4000000000012", "4000000000013", "4000000000015", "4000000000016"],
            importedProducts.Select(product => product.Code)
        );
        Assert.Equivalent(OatFlakes.ExpectedProduct, importedProducts[0]);
        Assert.Equivalent(
            new Product
            {
                Code = "4000000000015",
                Name = "Apfelschorle",
                IsPerHundredMilliliters = true,
                Fat = 0,
            },
            importedProducts[3]
        );
        Assert.Equivalent(new Product { Code = "4000000000016", Name = "Gewürzmischung" }, importedProducts[4]);

        var products = await ImportAndReadProductsAsync(
            CompressedExport.Create(
                [
                    OatFlakes.ExportLine,
                    CreateExportLine("4000000000012", "Apfelmark", 0.2),
                    CreateExportLine("4000000000014", "Käse", 28.5),
                ]
            )
        );

        Assert.Equal(["4000000000001", "4000000000012", "4000000000014"], products.Select(product => product.Code));
        Assert.Equivalent(OatFlakes.ExpectedProduct, products[0]);
        Assert.Equal("Apfelmark", products[1].Name);
        Assert.Equal(0.2, products[1].Fat);
    }

    [Theory]
    [MemberData(nameof(ProductChanges))]
    public async Task ImportAsync_ExistingProductChanged_UpdatesWholeProduct(
        string exportLineBefore,
        string exportLineAfter,
        Product expectedProduct
    )
    {
        await ImportAndReadProductsAsync(CompressedExport.Create([exportLineBefore]));

        var products = await ImportAndReadProductsAsync(CompressedExport.Create([exportLineAfter]));

        Assert.Equivalent(expectedProduct, Assert.Single(products));
    }

    [Fact]
    public async Task ImportAsync_TruncatedExport_ThrowsAndKeepsProducts()
    {
        var seededProducts = await SeedProductsAsync();
        using var host = ProductImportHost.Create(
            connectionString,
            CompressedExport.CreateTruncated(
                [CreateExportLine("4000000000013", "Butter", 83), CreateExportLine("4000000000014", "Käse", 28.5)]
            )
        );

        await Assert.ThrowsAsync<InvalidDataException>(() => ImportAsync(host));

        Assert.Equivalent(seededProducts, await ProductImportHost.ReadProductsAsync(host), strict: true);
    }

    [Theory]
    [MemberData(nameof(EmptyExports))]
    public async Task ImportAsync_EmptyExport_ThrowsAndKeepsProducts(byte[] export)
    {
        var seededProducts = await SeedProductsAsync();
        using var host = ProductImportHost.Create(connectionString, export);

        await Assert.ThrowsAsync<InvalidDataException>(() => ImportAsync(host));

        Assert.Equivalent(seededProducts, await ProductImportHost.ReadProductsAsync(host), strict: true);
    }

    [Fact]
    public async Task ImportAsync_MalformedGermanLine_ThrowsAndKeepsProducts()
    {
        const string malformedLine = """
            {"code":"4000000000099","countries_tags":["en:germany"],"product_name_de":
            """;
        var seededProducts = await SeedProductsAsync();
        using var host = ProductImportHost.Create(
            connectionString,
            CompressedExport.Create([CreateExportLine("4000000000011", "Apfelsaft", 0.3), malformedLine])
        );

        await Assert.ThrowsAnyAsync<JsonException>(() => ImportAsync(host));

        Assert.Equivalent(seededProducts, await ProductImportHost.ReadProductsAsync(host), strict: true);
    }

    [Fact]
    public async Task ImportAsync_ProductLimitReached_ImportsFirstProductsAndDeletesNothing()
    {
        await ImportAndReadProductsAsync(
            CompressedExport.Create(
                [
                    CreateExportLine("4000000000011", "Apfel", 0.2),
                    CreateExportLine("4000000000012", "Birne", 0.1),
                    CreateExportLine("4000000000013", "Butter", 83),
                ]
            )
        );

        var products = await ImportAndReadProductsAsync(
            CompressedExport.Create(
                [
                    CreateExportLine("4000000000014", "Käse", 28.5),
                    CreateExportLine("4000000000015", "Quark", 0.3),
                    CreateExportLine("4000000000016", "Sahne", 30),
                ]
            ),
            productLimit: 2
        );

        Assert.Equal(
            ["4000000000011", "4000000000012", "4000000000013", "4000000000014", "4000000000015"],
            products.Select(product => product.Code)
        );
    }

    [Fact]
    public async Task ImportAsync_DuplicateCode_KeepsLastOccurrence()
    {
        var products = await ImportAndReadProductsAsync(
            CompressedExport.Create(
                [CreateExportLine("4000000000011", "first", 1), CreateExportLine("4000000000011", "second", 2)]
            )
        );

        Assert.Equal("second", Assert.Single(products).Name);
    }

    [Fact]
    public async Task ImportAsync_LineLargerThanReadBuffer_ImportsAllProducts()
    {
        var largeLine = CreateExportLine("4000000000011", "Müsli", 6)
            .Insert(1, $"\"ingredients_text\":\"{new string('a', 1_500_000)}\",");

        var products = await ImportAndReadProductsAsync(
            CompressedExport.Create([largeLine, CreateExportLine("4000000000012", "Joghurt", 3.5)])
        );

        Assert.Equal(["4000000000011", "4000000000012"], products.Select(product => product.Code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_LastLineWithOrWithoutTrailingNewline_ImportsAllProducts(bool hasTrailingNewline)
    {
        var products = await ImportAndReadProductsAsync(
            CompressedExport.Create(
                [CreateExportLine("4000000000011", "Apfel", 0.2), CreateExportLine("4000000000012", "Birne", 0.1)],
                hasTrailingNewline
            )
        );

        Assert.Equal(["4000000000011", "4000000000012"], products.Select(product => product.Code));
    }

    private static string CreateExportLine(
        string code,
        string name,
        double fat,
        string per = "100g",
        string fatSource = "packaging",
        double? proteins = null
    )
    {
        var nutrients = new JsonObject { ["fat"] = CreateNutrient(fat, fatSource, per) };
        if (proteins is { } proteinsValue)
        {
            nutrients["proteins"] = CreateNutrient(proteinsValue, "packaging", per);
        }

        return new JsonObject
        {
            ["code"] = code,
            ["schema_version"] = 1004,
            ["countries_tags"] = new JsonArray("en:germany"),
            ["product_name_de"] = name,
            ["nutrition"] = new JsonObject
            {
                ["aggregated_set"] = new JsonObject
                {
                    ["per"] = per,
                    ["preparation"] = "as_sold",
                    ["nutrients"] = nutrients,
                },
            },
        }.ToJsonString();
    }

    private static JsonObject CreateNutrient(double value, string source, string per) =>
        new()
        {
            ["value"] = value,
            ["unit"] = "g",
            ["source"] = source,
            ["source_per"] = per,
        };

    private Task<Product[]> SeedProductsAsync() =>
        ImportAndReadProductsAsync(
            CompressedExport.Create(
                [CreateExportLine("4000000000011", "Apfel", 0.2), CreateExportLine("4000000000012", "Birne", 0.1)]
            )
        );

    private async Task<Product[]> ImportAndReadProductsAsync(byte[] export, int? productLimit = null)
    {
        using var host = ProductImportHost.Create(connectionString, export, productLimit);
        await ImportAsync(host);
        return await ProductImportHost.ReadProductsAsync(host);
    }

    private static async Task ImportAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<ProductImporter>()
            .ImportAsync(TestContext.Current.CancellationToken);
    }
}
