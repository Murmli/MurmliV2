using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Murmli.Backend.Sync.DataTypes;

public abstract class SyncFieldType<TValue>
{
    private protected SyncFieldType() { }

    internal abstract TValue DefaultOnCreate { get; }

    internal abstract bool IsNullable { get; }

    internal virtual bool HasDefaultOnCreate => true;

    internal abstract bool TryRead(JsonElement element, [MaybeNullWhen(false)] out TValue value);

    internal abstract void Write(Utf8JsonWriter writer, TValue value);
}
