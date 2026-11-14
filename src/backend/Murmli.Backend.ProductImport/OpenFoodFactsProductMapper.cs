using System.Globalization;
using System.Text.Json;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.ProductImport;

public static class OpenFoodFactsProductMapper
{
    private const string GermanyCountryTag = "en:germany";

    private const int FirstAggregatedNutritionSchemaVersion = 1003;

    public static Product? Map(JsonElement product)
    {
        if (!IsSoldInGermany(product))
        {
            return null;
        }

        var code = GetCleanText(product, "code");
        var name =
            GetCleanText(product, "product_name_de")
            ?? GetCleanText(product, "product_name_en")
            ?? GetCleanText(product, "product_name");
        if (code is null || name is null)
        {
            return null;
        }

        return HasAggregatedNutrition(product)
            ? MapAggregatedNutrition(product, code, name)
            : MapNutriments(product, code, name);
    }

    private static bool IsSoldInGermany(JsonElement product) =>
        GetProperty(product, "countries_tags", JsonValueKind.Array) is { } countries
        && countries
            .EnumerateArray()
            .Any(country => country.ValueKind == JsonValueKind.String && country.ValueEquals(GermanyCountryTag));

    private static bool HasAggregatedNutrition(JsonElement product) =>
        GetProperty(product, "schema_version", JsonValueKind.Number) is { } schemaVersion
        && schemaVersion.TryGetDouble(out var version)
        && version >= FirstAggregatedNutritionSchemaVersion;

    private static Product MapAggregatedNutrition(JsonElement product, string code, string name)
    {
        var aggregatedSet = GetObject(GetObject(product, "nutrition"), "aggregated_set");
        var nutrients =
            GetString(aggregatedSet, "preparation") == "as_sold" ? GetObject(aggregatedSet, "nutrients") : null;

        return CreateProduct(
            code,
            name,
            energyKilojoules: GetAggregatedNutrient(nutrients, "energy-kj"),
            energyKilocalories: GetAggregatedNutrient(nutrients, "energy-kcal"),
            fat: GetAggregatedNutrient(nutrients, "fat"),
            saturatedFat: GetAggregatedNutrient(nutrients, "saturated-fat"),
            carbohydrates: GetAggregatedNutrient(nutrients, "carbohydrates"),
            sugars: GetAggregatedNutrient(nutrients, "sugars"),
            addedSugars: GetAggregatedNutrient(nutrients, "added-sugars"),
            proteins: GetAggregatedNutrient(nutrients, "proteins"),
            isPerHundredMilliliters: GetString(aggregatedSet, "per") == "100ml"
        );
    }

    // "value_computed" is not converted to 100 g, so only "value" is read
    private static Nutrient GetAggregatedNutrient(JsonElement? nutrients, string key)
    {
        var nutrient = GetObject(nutrients, key);
        var value = GetNumber(nutrient, "value");
        return new Nutrient(value, value is not null && GetString(nutrient, "source") == "estimate");
    }

    private static Product MapNutriments(JsonElement product, string code, string name)
    {
        var nutriments = GetObject(product, "nutriments");

        return CreateProduct(
            code,
            name,
            energyKilojoules: new Nutrient(
                GetNumber(nutriments, "energy-kj_100g") ?? GetNumber(nutriments, "energy_100g"),
                IsEstimated: false
            ),
            energyKilocalories: GetNutriment(nutriments, "energy-kcal_100g"),
            fat: GetNutriment(nutriments, "fat_100g"),
            saturatedFat: GetNutriment(nutriments, "saturated-fat_100g"),
            carbohydrates: GetNutriment(nutriments, "carbohydrates_100g"),
            sugars: GetNutriment(nutriments, "sugars_100g"),
            addedSugars: GetNutriment(nutriments, "added-sugars_100g"),
            proteins: GetNutriment(nutriments, "proteins_100g"),
            isPerHundredMilliliters: GetQuantityUnit(product) == "ml"
        );
    }

    private static Nutrient GetNutriment(JsonElement? nutriments, string propertyName) =>
        new(GetNumber(nutriments, propertyName), IsEstimated: false);

    // Mirrors the unit detection of the Open Food Facts schema migration
    private static string? GetQuantityUnit(JsonElement product) =>
        GetString(product, "product_quantity_unit") is { Length: > 0 } productQuantityUnit
            ? productQuantityUnit
            : GetString(product, "serving_quantity_unit");

    private static Product CreateProduct(
        string code,
        string name,
        Nutrient energyKilojoules,
        Nutrient energyKilocalories,
        Nutrient fat,
        Nutrient saturatedFat,
        Nutrient carbohydrates,
        Nutrient sugars,
        Nutrient addedSugars,
        Nutrient proteins,
        bool isPerHundredMilliliters
    )
    {
        Nutrient[] nutrients =
        [
            energyKilojoules,
            energyKilocalories,
            fat,
            saturatedFat,
            carbohydrates,
            sugars,
            addedSugars,
            proteins,
        ];

        return new Product
        {
            Code = code,
            Name = name,
            IsPerHundredMilliliters = isPerHundredMilliliters && nutrients.Any(nutrient => nutrient.Value is not null),
            EnergyKilojoules = energyKilojoules.Value,
            EnergyKilojoulesIsEstimated = energyKilojoules.IsEstimated,
            EnergyKilocalories = energyKilocalories.Value,
            EnergyKilocaloriesIsEstimated = energyKilocalories.IsEstimated,
            Fat = fat.Value,
            FatIsEstimated = fat.IsEstimated,
            SaturatedFat = saturatedFat.Value,
            SaturatedFatIsEstimated = saturatedFat.IsEstimated,
            Carbohydrates = carbohydrates.Value,
            CarbohydratesIsEstimated = carbohydrates.IsEstimated,
            Sugars = sugars.Value,
            SugarsIsEstimated = sugars.IsEstimated,
            AddedSugars = addedSugars.Value,
            AddedSugarsIsEstimated = addedSugars.IsEstimated,
            Proteins = proteins.Value,
            ProteinsIsEstimated = proteins.IsEstimated,
        };
    }

    // PostgreSQL text cannot store '\0', a single one would fail the whole COPY
    private static string? GetCleanText(JsonElement product, string propertyName)
    {
        var text = GetString(product, propertyName)?.Replace("\0", string.Empty).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static double? GetNumber(JsonElement? element, string propertyName)
    {
        double? number = GetProperty(element, propertyName) switch
        {
            { ValueKind: JsonValueKind.Number } numberElement when numberElement.TryGetDouble(out var value) => value,
            { ValueKind: JsonValueKind.String } textElement
                when double.TryParse(
                    textElement.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value
                ) => value,
            _ => null,
        };
        return number.HasValue && double.IsFinite(number.Value) ? number : null;
    }

    private static string? GetString(JsonElement? element, string propertyName) =>
        GetProperty(element, propertyName, JsonValueKind.String)?.GetString();

    private static JsonElement? GetObject(JsonElement? element, string propertyName) =>
        GetProperty(element, propertyName, JsonValueKind.Object);

    private static JsonElement? GetProperty(JsonElement? element, string propertyName, JsonValueKind valueKind) =>
        GetProperty(element, propertyName) is { } value && value.ValueKind == valueKind ? value : null;

    private static JsonElement? GetProperty(JsonElement? element, string propertyName) =>
        element is { ValueKind: JsonValueKind.Object } container && container.TryGetProperty(propertyName, out var value)
            ? value
            : null;

    private readonly record struct Nutrient(double? Value, bool IsEstimated);
}
