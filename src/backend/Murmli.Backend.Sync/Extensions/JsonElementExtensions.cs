using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Murmli.Backend.Sync.Extensions;

internal static class JsonElementExtensions
{
    private const long MinimumTime = -62_135_596_800_000;

    private const long MaximumTime = 253_402_300_799_999;

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

    public static bool TryGetUnixMilliseconds(this JsonElement element, out DateTimeOffset value)
    {
        value = default;
        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var milliseconds)
            || milliseconds is < MinimumTime or > MaximumTime
        )
        {
            return false;
        }

        value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return true;
    }
}
