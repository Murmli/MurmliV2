using System.Linq.Expressions;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

public sealed class SyncFieldGroupBuilder<TRecord>
    where TRecord : SyncRecord
{
    private readonly List<SyncField<TRecord>> fields = [];

    internal SyncFieldGroupBuilder() { }

    internal IReadOnlyList<SyncField<TRecord>> Fields => fields;

    public SyncFieldBuilder Field<TValue>(string name, Expression<Func<TRecord, TValue>> value, SyncFieldType<TValue> type)
    {
        var field = new SyncField<TRecord, TValue>(
            name,
            new SyncPropertyAccessor<TRecord, TValue>(value),
            type,
            isSentToDevices: true,
            SyncServerFill.None
        );
        fields.Add(field);
        return new SyncFieldBuilder(field.MarkRequiredOnCreate);
    }
}
