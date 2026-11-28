using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Murmli.Backend.Sync.Extensions;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class KeyFieldType(FrozenSet<string> keys) : SyncFieldType<string>
{
    internal override string DefaultOnCreate => "";

    internal override bool IsNullable => false;

    internal override bool HasDefaultOnCreate => false;

    internal override bool TryRead(JsonElement element, [MaybeNullWhen(false)] out string value) =>
        element.TryGetReadableString(out value) && keys.Contains(value);

    internal override void Write(Utf8JsonWriter writer, string value) => writer.WriteStringValue(value);
}
