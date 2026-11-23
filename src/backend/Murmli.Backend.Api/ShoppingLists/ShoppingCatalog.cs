namespace Murmli.Backend.Api.ShoppingLists;

internal static class ShoppingCatalog
{
    public static IReadOnlyCollection<string> Categories { get; } =
    [
        "bakery",
        "bakingAndCooking",
        "beverages",
        "brothsAndSauces",
        "cannedGoods",
        "cerealsAndMuesli",
        "dairy",
        "frozen",
        "fruits",
        "grainsAndPasta",
        "householdGoods",
        "meatAndFish",
        "miscellaneous",
        "nuts",
        "personalCare",
        "petFood",
        "refrigerated",
        "snacks",
        "spices",
        "sweets",
        "vegetables",
        "vinegarAndOils",
    ];

    public static IReadOnlyCollection<string> Units { get; } =
        ["can", "gram", "kilogram", "liter", "milliliter", "package", "piece", "pinch", "slice", "tablespoon", "teaspoon"];
}
