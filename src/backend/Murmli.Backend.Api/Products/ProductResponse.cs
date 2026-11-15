namespace Murmli.Backend.Api.Products;

public sealed record ProductResponse(
    string Code,
    string Name,
    string? Brand,
    bool IsPerHundredMilliliters,
    NutrientResponse? EnergyKilojoules,
    NutrientResponse? EnergyKilocalories,
    NutrientResponse? Fat,
    NutrientResponse? SaturatedFat,
    NutrientResponse? Carbohydrates,
    NutrientResponse? Sugars,
    NutrientResponse? AddedSugars,
    NutrientResponse? Proteins
);
