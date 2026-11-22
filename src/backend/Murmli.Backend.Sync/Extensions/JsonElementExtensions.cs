using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Murmli.Backend.Sync.Extensions;

internal static class JsonElementExtensions
{
    public static bool TryGetReadableString(this JsonElement element, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            value = element.GetString();
        }
        catch (InvalidOperationException)
        {
            // A lone surrogate escape cannot be read
            return false;
        }

        // Postgres rejects U+0000 in text
        return value is not null && !value.Contains('\0');
    }
}
