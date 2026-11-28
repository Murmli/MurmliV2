using System.Text.Json;
using Murmli.Backend.Sync.Extensions;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class TimestampFieldType : SyncFieldType<DateTimeOffset>
{
    internal override DateTimeOffset DefaultOnCreate => default;

    internal override bool IsNullable => false;

    internal override bool HasDefaultOnCreate => false;

    internal override bool TryRead(JsonElement element, out DateTimeOffset value) => element.TryGetUnixMilliseconds(out value);

    internal override void Write(Utf8JsonWriter writer, DateTimeOffset value) => writer.WriteNumberValue(value.ToUnixTimeMilliseconds());
}
