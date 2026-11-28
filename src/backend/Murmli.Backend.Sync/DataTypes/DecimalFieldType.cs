using System.Text.Json;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class DecimalFieldType(decimal exclusiveMinimum, decimal maximum, int maximumScale) : SyncFieldType<decimal?>
{
    // Dividing by one with 28 fractional zeros yields the smallest scale, so 2.0 is written as 2
    private const decimal ScaleNormalizer = 1.0000000000000000000000000000m;

    internal override decimal? DefaultOnCreate => null;

    internal override bool IsNullable => true;

    internal override bool TryRead(JsonElement element, out decimal? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetDecimal(out var number)
            || number <= exclusiveMinimum
            || number > maximum
            || decimal.Round(number, maximumScale) != number
        )
        {
            return false;
        }

        value = number;
        return true;
    }

    internal override void Write(Utf8JsonWriter writer, decimal? value)
    {
        if (value is { } number)
        {
            writer.WriteNumberValue(number / ScaleNormalizer);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
