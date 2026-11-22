using System.Collections.Frozen;

namespace Murmli.Backend.Sync.DataTypes;

public static class SyncFieldTypes
{
    private const int MaximumDecimalScale = 28;

    public static SyncFieldType<string> Text(int minimumLength, int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumLength, maximumLength);
        return new TextFieldType(minimumLength, maximumLength);
    }

    public static SyncFieldType<string?> NullableKey(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0)
        {
            throw new ArgumentException("A key field needs at least one key.", nameof(keys));
        }

        return new NullableKeyFieldType(keys.ToFrozenSet(StringComparer.Ordinal));
    }

    public static SyncFieldType<decimal?> Decimal(decimal exclusiveMinimum, decimal maximum, int maximumScale)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(exclusiveMinimum, maximum);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumScale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumScale, MaximumDecimalScale);
        return new DecimalFieldType(exclusiveMinimum, maximum, maximumScale);
    }

    public static SyncFieldType<bool> Boolean() => new BooleanFieldType();
}
