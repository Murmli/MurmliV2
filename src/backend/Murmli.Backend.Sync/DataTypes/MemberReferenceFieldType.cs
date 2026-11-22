using System.Text.Json;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class MemberReferenceFieldType : SyncFieldType<Guid?>
{
    internal override Guid? DefaultOnCreate => null;

    internal override bool IsNullable => true;

    internal override bool IsMemberReference => true;

    internal override bool TryRead(JsonElement element, out Guid? value)
    {
        value = null;
        return false;
    }

    internal override void Write(Utf8JsonWriter writer, Guid? value)
    {
        if (value is { } id)
        {
            writer.WriteStringValue(id);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
