using System.Text.Json;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed record ShoppingCatalogFile(IReadOnlyList<string> Categories, IReadOnlyList<string> Units)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerOptions.Strict)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<ShoppingCatalogFile> ReadAsync()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SyncCases", "shopping", "catalog.json"));
        return Assert.IsType<ShoppingCatalogFile>(
            await JsonSerializer.DeserializeAsync<ShoppingCatalogFile>(stream, SerializerOptions, TestContext.Current.CancellationToken)
        );
    }
}
