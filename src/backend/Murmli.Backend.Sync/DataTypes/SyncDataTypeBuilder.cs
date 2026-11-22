using System.Linq.Expressions;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

public sealed class SyncDataTypeBuilder<TRecord>
    where TRecord : SyncRecord
{
    private readonly List<SyncFieldGroup<TRecord>> groups = [];

    private readonly List<SyncField<TRecord>> declaredFields = [];

    private SyncDataTypeDefinition definition = new("", "", 0);

    internal SyncDataTypeBuilder() { }

    public SyncDataTypeBuilder<TRecord> Named(string name, string scopeKind)
    {
        definition = definition with { Name = name, ScopeKind = scopeKind };
        return this;
    }

    public SyncDataTypeBuilder<TRecord> MaxRecordsPerScope(int maximum)
    {
        definition = definition with { MaxRecordsPerScope = maximum };
        return this;
    }

    public SyncFieldBuilder Field<TValue>(
        string name,
        Expression<Func<TRecord, TValue>> value,
        Expression<Func<TRecord, DateTimeOffset>> changedAt,
        SyncFieldType<TValue> type
    )
    {
        var field = new SyncField<TRecord, TValue>(name, new SyncPropertyAccessor<TRecord, TValue>(value), type, isSentToDevices: true);
        groups.Add(new SyncFieldGroup<TRecord>(name, new SyncPropertyAccessor<TRecord, DateTimeOffset>(changedAt), [field]));
        declaredFields.Add(field);
        return new SyncFieldBuilder(field.MarkRequiredOnCreate);
    }

    public void Group(string name, Expression<Func<TRecord, DateTimeOffset>> changedAt, Action<SyncFieldGroupBuilder<TRecord>> fields)
    {
        var group = new SyncFieldGroupBuilder<TRecord>();
        fields(group);
        groups.Add(new SyncFieldGroup<TRecord>(name, new SyncPropertyAccessor<TRecord, DateTimeOffset>(changedAt), [.. group.Fields]));
        declaredFields.AddRange(group.Fields);
    }

    public void ServerField<TValue>(
        string name,
        Expression<Func<TRecord, TValue>> value,
        SyncFieldType<TValue> type,
        bool isSentToDevices = true
    ) => declaredFields.Add(new SyncField<TRecord, TValue>(name, new SyncPropertyAccessor<TRecord, TValue>(value), type, isSentToDevices));

    internal SyncDataTypeLayout<TRecord> Build() => new(definition, [.. groups], [.. declaredFields]);
}
