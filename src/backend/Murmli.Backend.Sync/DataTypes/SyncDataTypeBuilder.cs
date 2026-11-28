using System.Linq.Expressions;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

public sealed class SyncDataTypeBuilder<TRecord>
    where TRecord : SyncRecord
{
    private readonly List<SyncFieldGroup<TRecord>> groups = [];

    private readonly List<SyncField<TRecord>> declaredFields = [];

    private readonly List<SyncField<TRecord>> serverFields = [];

    private SyncDataTypeDefinition definition = new("", "", 0, null);

    internal SyncDataTypeBuilder() { }

    public SyncDataTypeBuilder<TRecord> Named(string name, string scopeKind)
    {
        definition = definition with { Name = name, ScopeKind = scopeKind };
        return this;
    }

    public SyncDataTypeBuilder<TRecord> ScopeRoot(int maximumOwnedScopesPerAccount)
    {
        definition = definition with { MaximumOwnedScopesPerAccount = maximumOwnedScopesPerAccount };
        return this;
    }

    public SyncDataTypeBuilder<TRecord> MaximumRecordsPerScope(int maximum)
    {
        definition = definition with { MaximumRecordsPerScope = maximum };
        return this;
    }

    public SyncFieldBuilder Field<TValue>(
        string name,
        Expression<Func<TRecord, TValue>> value,
        Expression<Func<TRecord, DateTimeOffset>> changedAt,
        SyncFieldType<TValue> type
    )
    {
        var field = new SyncField<TRecord, TValue>(
            name,
            new SyncPropertyAccessor<TRecord, TValue>(value),
            type,
            isSentToDevices: true,
            SyncServerFill.None
        );
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
        SyncServerFill fill = SyncServerFill.None,
        bool isSentToDevices = true
    )
    {
        var field = new SyncField<TRecord, TValue>(name, new SyncPropertyAccessor<TRecord, TValue>(value), type, isSentToDevices, fill);
        declaredFields.Add(field);
        serverFields.Add(field);
    }

    internal SyncDataTypeLayout<TRecord> Build() => new(definition, [.. groups], [.. declaredFields], [.. serverFields]);
}
