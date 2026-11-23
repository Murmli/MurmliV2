using System.Text.Json;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class SyncServerFields
{
    public static IReadOnlyDictionary<string, JsonElement> Of(params (string Name, object? Value)[] fields) =>
        fields.ToDictionary(field => field.Name, field => JsonSerializer.SerializeToElement(field.Value), StringComparer.Ordinal);
}
