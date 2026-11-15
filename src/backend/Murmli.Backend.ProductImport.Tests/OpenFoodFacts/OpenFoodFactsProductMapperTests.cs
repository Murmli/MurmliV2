using System.Text.Json;
using Murmli.Backend.Data.Products;
using Murmli.Backend.ProductImport.OpenFoodFacts;
using Murmli.Backend.ProductImport.Tests.Fixtures;

namespace Murmli.Backend.ProductImport.Tests.OpenFoodFacts;

public class OpenFoodFactsProductMapperTests
{
    [Fact]
    public void Map_AggregatedNutritionWithPackagingAndEstimatedValues_ReturnsValuesWithEstimateFlags() =>
        Assert.Equivalent(OatFlakes.ExpectedProduct, Map(OatFlakes.ExportLine));

    [Fact]
    public void Map_AggregatedNutritionPerHundredMilliliters_ReturnsProductPerHundredMilliliters()
    {
        const string exportLine = """
            {"code":"4000000000002","schema_version":1003,"countries_tags":["en:austria","en:germany"],"product_name_de":"Apfelschorle","nutrition":{"aggregated_set":{"per":"100ml","preparation":"as_sold","nutrients":{"energy-kcal":{"value":24,"unit":"kcal","source":"packaging","source_per":"100ml"},"sugars":{"value":5.5,"unit":"g","source":"packaging","source_per":"100ml"}}}}}
            """;

        Assert.Equivalent(
            new Product
            {
                Code = "4000000000002",
                Name = "Apfelschorle",
                IsPerHundredMilliliters = true,
                EnergyKilocalories = 24,
                Sugars = 5.5,
            },
            Map(exportLine)
        );
    }

    [Fact]
    public void Map_PreparedAggregatedNutrition_ReturnsProductWithoutNutrients()
    {
        const string exportLine = """
            {"code":"4000000000003","schema_version":1004,"countries_tags":["en:germany"],"product_name_de":"Puddingpulver","nutrition":{"aggregated_set":{"per":"100g","preparation":"prepared","nutrients":{"energy-kcal":{"value":95,"unit":"kcal","source":"packaging","source_per":"100g"}}}}}
            """;

        Assert.Equivalent(new Product { Code = "4000000000003", Name = "Puddingpulver" }, Map(exportLine));
    }

    [Fact]
    public void Map_AggregatedNutrientWithOnlyComputedValue_IgnoresNutrient()
    {
        const string exportLine = """
            {"code":"4000000000009","schema_version":1004,"countries_tags":["en:germany"],"product_name_de":"Riegel","nutrition":{"aggregated_set":{"per":"100g","preparation":"as_sold","nutrients":{"energy-kj":{"value_computed":400,"unit":"kJ","source":"estimate","source_per":"serving"},"fat":{"value":20,"unit":"g","source":"packaging","source_per":"100g"}}}}}
            """;

        Assert.Equivalent(
            new Product
            {
                Code = "4000000000009",
                Name = "Riegel",
                IsPerHundredMilliliters = false,
                Fat = 20,
            },
            Map(exportLine)
        );
    }

    [Theory]
    [InlineData("product_quantity_unit")]
    [InlineData("serving_quantity_unit")]
    public void Map_NutrimentsWithoutSchemaVersionAndMilliliterQuantity_ReturnsNutrimentsPerHundredMilliliters(
        string quantityUnitPropertyName
    )
    {
        var exportLine = $$$"""
            {"code":"4000000000004","countries_tags":["en:germany"],"product_name":"Vollmilch","product_name_de":"Vollmilch","{{{quantityUnitPropertyName}}}":"ml","nutriments":{"energy_100g":272,"energy-kcal_100g":65,"fat_100g":3.5,"saturated-fat_100g":2.3,"carbohydrates_100g":4.8,"sugars_100g":4.8,"proteins_100g":3.4}}
            """;

        Assert.Equivalent(
            new Product
            {
                Code = "4000000000004",
                Name = "Vollmilch",
                IsPerHundredMilliliters = true,
                EnergyKilojoules = 272,
                EnergyKilocalories = 65,
                Fat = 3.5,
                SaturatedFat = 2.3,
                Carbohydrates = 4.8,
                Sugars = 4.8,
                Proteins = 3.4,
            },
            Map(exportLine)
        );
    }

    [Fact]
    public void Map_NutrimentsWithKilojoulesAndUnspecifiedEnergy_ReturnsKilojoules()
    {
        const string exportLine = """
            {"code":"4000000000005","schema_version":1002,"countries_tags":["en:germany"],"product_name_en":"Dark chocolate","nutriments":{"energy-kj_100g":2280,"energy_100g":2290}}
            """;

        Assert.Equal(2280, Map(exportLine)?.EnergyKilojoules);
    }

    [Fact]
    public void Map_NutrimentAsNumericText_ReturnsParsedValue()
    {
        const string exportLine = """
            {"code":"4000000000005","schema_version":1002,"countries_tags":["en:germany"],"product_name_en":"Dark chocolate","nutriments":{"fat_100g":"31.5"}}
            """;

        Assert.Equal(31.5, Map(exportLine)?.Fat);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Map_NutrimentAsNonFiniteText_ReturnsNoValue(string nonFiniteText)
    {
        var exportLine = $$$"""
            {"code":"4000000000005","schema_version":1002,"countries_tags":["en:germany"],"product_name_en":"Dark chocolate","nutriments":{"sugars_100g":"{{{nonFiniteText}}}"}}
            """;

        Assert.Null(Assert.IsType<Product>(Map(exportLine)).Sugars);
    }

    [Fact]
    public void Map_GermanyOnlyInOrigins_ReturnsNull()
    {
        const string exportLine = """
            {"code":"3000000000001","countries_tags":["en:france"],"origins_tags":["en:germany"],"product_name":"Fromage"}
            """;

        Assert.Null(Map(exportLine));
    }

    [Fact]
    public void Map_NoNutritionData_ReturnsProductWithoutNutrients()
    {
        const string exportLine = """
            {"code":"4000000000006","countries_tags":["en:germany"],"product_name_de":"Gewürzmischung"}
            """;

        Assert.Equivalent(new Product { Code = "4000000000006", Name = "Gewürzmischung" }, Map(exportLine));
    }

    [Theory]
    [InlineData("Brot", "Bread", "Pain", "Brot")]
    [InlineData("   ", "Bread", "Pain", "Bread")]
    [InlineData("", "", "Pain", "Pain")]
    [InlineData(null, null, "Pain", "Pain")]
    [InlineData("Br\u0000ot ", null, null, "Brot")]
    public void Map_ProductNames_ReturnsFirstUsableCleanedName(
        string? germanName,
        string? englishName,
        string? name,
        string expectedName
    ) =>
        Assert.Equal(
            expectedName,
            Map(CreateExportLine(germanName: germanName, englishName: englishName, name: name))?.Name
        );

    [Fact]
    public void Map_NoUsableName_ReturnsNull() =>
        Assert.Null(Map(CreateExportLine(germanName: "", englishName: " ")));

    [Fact]
    public void Map_CodeWithSurroundingWhitespace_ReturnsTrimmedCode() =>
        Assert.Equal("4000000000010", Map(CreateExportLine(code: "  4000000000010 ", germanName: "Brot"))?.Code);

    [Fact]
    public void Map_EmptyCode_ReturnsNull() => Assert.Null(Map(CreateExportLine(code: "", germanName: "Brot")));

    [Theory]
    [InlineData("ja!, Rewe, ja! Rewe", "ja!, Rewe, ja! Rewe")]
    [InlineData(" Re\u0000we ", "Rewe")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Map_Brands_ReturnsCleanedBrandText(string? brands, string? expectedBrand)
    {
        var product = Map(CreateExportLine(germanName: "Brot", brands: brands));

        Assert.NotNull(product);
        Assert.Equal(expectedBrand, product.Brand);
    }

    [Theory]
    [InlineData("Gefüllte Halbbitterschokolade", "Dark chocolate", "Gefüllte Halbbitterschokolade")]
    [InlineData(null, "Dark chocolate", "Dark chocolate")]
    [InlineData("  ", "Dark chocolate", "Dark chocolate")]
    [InlineData(" Scho\u0000kolade ", null, "Schokolade")]
    [InlineData("", "", null)]
    [InlineData(null, null, null)]
    public void Map_GenericName_ReturnsGermanOrMainLanguageGenericName(
        string? germanGenericName,
        string? genericName,
        string? expectedGenericName
    )
    {
        var product = Map(CreateExportLine(germanName: "Brot", germanGenericName: germanGenericName, genericName: genericName));

        Assert.NotNull(product);
        Assert.Equal(expectedGenericName, product.GenericName);
    }

    [Theory]
    [InlineData(25950000039, 25950000039d)]
    [InlineData("12", 12d)]
    [InlineData(null, null)]
    [InlineData("abc", null)]
    public void Map_PopularityKey_ReturnsNumber(object? popularityKey, double? expectedPopularityKey)
    {
        var product = Map(CreateExportLine(germanName: "Brot", popularityKey: popularityKey));

        Assert.NotNull(product);
        Assert.Equal(expectedPopularityKey, product.PopularityKey);
    }

    private static Product? Map(string exportLine)
    {
        using var document = JsonDocument.Parse(exportLine);
        return OpenFoodFactsProductMapper.Map(document.RootElement);
    }

    private static string CreateExportLine(
        string code = "4000000000010",
        string? germanName = null,
        string? englishName = null,
        string? name = null,
        string? brands = null,
        string? germanGenericName = null,
        string? genericName = null,
        object? popularityKey = null
    )
    {
        Dictionary<string, object?> product = new()
        {
            ["code"] = code,
            ["countries_tags"] = new[] { "en:germany" },
            ["product_name_de"] = germanName,
            ["product_name_en"] = englishName,
            ["product_name"] = name,
            ["brands"] = brands,
            ["generic_name_de"] = germanGenericName,
            ["generic_name"] = genericName,
            ["popularity_key"] = popularityKey,
        };
        return JsonSerializer.Serialize(product.Where(property => property.Value is not null).ToDictionary());
    }
}
