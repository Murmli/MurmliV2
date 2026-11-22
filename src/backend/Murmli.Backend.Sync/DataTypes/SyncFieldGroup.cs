using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class SyncFieldGroup<TRecord>(
    string name,
    SyncPropertyAccessor<TRecord, DateTimeOffset> changedAt,
    IReadOnlyList<SyncField<TRecord>> fields
)
    where TRecord : SyncRecord
{
    public string Name { get; } = name;

    public string ChangedAtPropertyName => changedAt.PropertyName;

    public IReadOnlyList<SyncField<TRecord>> Fields { get; } = fields;

    public DateTimeOffset GetChangedAt(TRecord record) => changedAt.Get(record);

    public void SetChangedAt(TRecord record, DateTimeOffset time) => changedAt.Set(record, time);
}
