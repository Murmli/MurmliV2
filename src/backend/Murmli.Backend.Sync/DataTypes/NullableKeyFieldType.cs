using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Murmli.Backend.Sync.Extensions;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class NullableKeyFieldType(FrozenSet<string> keys) : SyncFieldType<string?>
{
    internal override string? DefaultOnCreate => null;

    internal override bool IsNullable => true;

    internal override bool TryRead(JsonElement element, [MaybeNullWhen(false)] out string? value)
    {
        value = null;
        return element.ValueKind == JsonValueKind.Null || (element.TryGetReadableString(out value) && keys.Contains(value));
    }

    internal override void Write(Utf8JsonWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
