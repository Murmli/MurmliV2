using Murmli.Backend.Data.Products;

namespace Murmli.Backend.ProductImport.Tests.Fixtures;

internal static class OatFlakes
{
    public const string ExportLine = """
        {"code":"4000000000001","schema_version":1004,"countries_tags":["en:germany"],"product_name":"Haferflocken","product_name_de":"Haferflocken","product_name_en":"Oat flakes","nutrition":{"aggregated_set":{"per":"100g","preparation":"as_sold","nutrients":{"energy-kj":{"value":1572,"value_computed":1572,"unit":"kJ","source":"estimate","source_per":"100g"},"energy-kcal":{"value":372,"modifier":"~","unit":"kcal","source":"packaging","source_per":"100g"},"fat":{"value":7,"unit":"g","source":"estimate","source_per":"100g"},"saturated-fat":{"value":1.3,"unit":"g","source":"packaging","source_per":"100g"},"carbohydrates":{"value":58.7,"unit":"g","source":"estimate","source_per":"100g"},"sugars":{"value":0.5,"modifier":"<","unit":"g","source":"packaging","source_per":"100g"},"added-sugars":{"value":0,"modifier":"~","unit":"g","source":"estimate","source_per":"100g"},"proteins":{"value":13.5,"unit":"g","source":"packaging","source_per":"100g"}}}},"nutriments":{}}
        """;

    public static Product ExpectedProduct =>
        new()
        {
            Code = "4000000000001",
            Name = "Haferflocken",
            IsPerHundredMilliliters = false,
            EnergyKilojoules = 1572,
            EnergyKilojoulesIsEstimated = true,
            EnergyKilocalories = 372,
            Fat = 7,
            FatIsEstimated = true,
            SaturatedFat = 1.3,
            Carbohydrates = 58.7,
            CarbohydratesIsEstimated = true,
            Sugars = 0.5,
            AddedSugars = 0,
            AddedSugarsIsEstimated = true,
            Proteins = 13.5,
        };
}
