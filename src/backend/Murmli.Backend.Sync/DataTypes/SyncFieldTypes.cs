using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Murmli.Backend.Sync.DataTypes;

public static class SyncFieldTypes
{
    private const int MaximumDecimalScale = 28;

    public static SyncFieldType<string> Text(int minimumLength, int maximumLength, Regex? pattern = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumLength, maximumLength);
        return new TextFieldType(minimumLength, maximumLength, pattern);
    }

    public static SyncFieldType<string> Key(IReadOnlyCollection<string> keys) => new KeyFieldType(ToKeySet(keys));

    public static SyncFieldType<string?> NullableKey(IReadOnlyCollection<string> keys) => new NullableKeyFieldType(ToKeySet(keys));

    public static SyncFieldType<decimal?> Decimal(decimal exclusiveMinimum, decimal maximum, int maximumScale)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(exclusiveMinimum, maximum);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumScale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumScale, MaximumDecimalScale);
        return new DecimalFieldType(exclusiveMinimum, maximum, maximumScale);
    }

    public static SyncFieldType<bool> Boolean() => new BooleanFieldType();

    public static SyncFieldType<DateTimeOffset> Timestamp() => new TimestampFieldType();

    public static SyncFieldType<Guid?> MemberReference() => new MemberReferenceFieldType();

    private static FrozenSet<string> ToKeySet(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0)
        {
            throw new ArgumentException("A key field needs at least one key.", nameof(keys));
        }

        return keys.ToFrozenSet(StringComparer.Ordinal);
    }
}
