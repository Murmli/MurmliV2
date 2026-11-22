using System.Text.Json;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class BooleanFieldType : SyncFieldType<bool>
{
    internal override bool DefaultOnCreate => false;

    internal override bool IsNullable => false;

    internal override bool TryRead(JsonElement element, out bool value)
    {
        value = element.ValueKind == JsonValueKind.True;
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    internal override void Write(Utf8JsonWriter writer, bool value) => writer.WriteBooleanValue(value);
}
