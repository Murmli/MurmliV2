namespace Murmli.Backend.Data.Products;

/// <summary>
/// Nutrient values refer to 100 g of the product, or to 100 ml when <see cref="IsPerHundredMilliliters"/> is true.
/// Energy is in kJ and kcal, all other nutrients in grams.
/// </summary>
public class Product
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool IsPerHundredMilliliters { get; set; }

    public double? EnergyKilojoules { get; set; }

    public bool EnergyKilojoulesIsEstimated { get; set; }

    public double? EnergyKilocalories { get; set; }

    public bool EnergyKilocaloriesIsEstimated { get; set; }

    public double? Fat { get; set; }

    public bool FatIsEstimated { get; set; }

    public double? SaturatedFat { get; set; }

    public bool SaturatedFatIsEstimated { get; set; }

    public double? Carbohydrates { get; set; }

    public bool CarbohydratesIsEstimated { get; set; }

    public double? Sugars { get; set; }

    public bool SugarsIsEstimated { get; set; }

    public double? AddedSugars { get; set; }

    public bool AddedSugarsIsEstimated { get; set; }

    public double? Proteins { get; set; }

    public bool ProteinsIsEstimated { get; set; }
}
