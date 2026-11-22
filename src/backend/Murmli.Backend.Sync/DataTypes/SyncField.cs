using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

internal abstract class SyncField<TRecord>(string name, bool isSentToDevices)
    where TRecord : SyncRecord
{
    public string Name { get; } = name;

    public bool IsSentToDevices { get; } = isSentToDevices;

    public bool IsRequiredOnCreate { get; private set; }

    public abstract string PropertyName { get; }

    public abstract bool IsNullable { get; }

    public abstract bool HasDefaultOnCreate { get; }

    public void MarkRequiredOnCreate() => IsRequiredOnCreate = true;

    public abstract bool TryRead(JsonElement element, [NotNullWhen(true)] out Action<TRecord>? assignment);

    public abstract void SetDefault(TRecord record);

    public abstract void Write(Utf8JsonWriter writer, TRecord record);
}

internal sealed class SyncField<TRecord, TValue>(
    string name,
    SyncPropertyAccessor<TRecord, TValue> accessor,
    SyncFieldType<TValue> type,
    bool isSentToDevices
) : SyncField<TRecord>(name, isSentToDevices)
    where TRecord : SyncRecord
{
    public override string PropertyName => accessor.PropertyName;

    public override bool IsNullable => type.IsNullable;

    public override bool HasDefaultOnCreate => type.HasDefaultOnCreate;

    public override bool TryRead(JsonElement element, [NotNullWhen(true)] out Action<TRecord>? assignment)
    {
        if (!type.TryRead(element, out var value))
        {
            assignment = null;
            return false;
        }

        assignment = record => accessor.Set(record, value);
        return true;
    }

    public override void SetDefault(TRecord record) => accessor.Set(record, type.DefaultOnCreate);

    public override void Write(Utf8JsonWriter writer, TRecord record)
    {
        writer.WritePropertyName(Name);
        type.Write(writer, accessor.Get(record));
    }
}
