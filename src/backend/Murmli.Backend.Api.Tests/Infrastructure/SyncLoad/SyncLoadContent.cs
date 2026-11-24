using System.Text.Json.Nodes;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal sealed class SyncLoadContent
{
    private static readonly string[] ProductNames =
    [
        "Milch",
        "Vollkornbrot",
        "Äpfel",
        "Bananen",
        "Butter",
        "Gouda",
        "Joghurt",
        "Eier",
        "Tomaten",
        "Gurke",
        "Spaghetti",
        "Basmatireis",
        "Kaffeebohnen",
        "Pfefferminztee",
        "Weizenmehl",
        "Zucker",
        "Meersalz",
        "Olivenöl",
        "Spülmittel",
        "Zahnpasta",
    ];

    private readonly Random random;

    private readonly ShoppingCatalogFile catalog;

    private SyncLoadContent(Random random, ShoppingCatalogFile catalog)
    {
        this.random = random;
        this.catalog = catalog;
    }

    public static async Task<SyncLoadContent> CreateAsync(int seed) => new(new Random(seed), await ShoppingCatalogFile.ReadAsync());

    public JsonObject CreateItemFields() =>
        new()
        {
            ["name"] = CreateItemName(),
            ["quantity"] = random.Next(4) == 0 ? null : JsonValue.Create(random.Next(1, 10)),
            ["unit"] = random.Next(4) == 0 ? null : JsonValue.Create(catalog.Units[random.Next(catalog.Units.Count)]),
            ["category"] = catalog.Categories[random.Next(catalog.Categories.Count)],
            ["language"] = "de",
        };

    private string CreateItemName() => $"{ProductNames[random.Next(ProductNames.Length)]} {random.Next(1, 100)}";
}
