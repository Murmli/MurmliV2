using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class SyncDataTypeLayout<TRecord>
    where TRecord : SyncRecord
{
    public SyncDataTypeLayout(SyncDataTypeDefinition definition, IReadOnlyList<SyncFieldGroup<TRecord>> groups, IReadOnlyList<SyncField<TRecord>> fields)
    {
        Definition = definition;
        Groups = groups;
        Fields = fields;
        var deviceFields = new Dictionary<string, SyncField<TRecord>>(StringComparer.Ordinal);
        foreach (var field in groups.SelectMany(group => group.Fields))
        {
            deviceFields.TryAdd(field.Name, field);
        }

        DeviceFields = deviceFields;
        SentFields = [.. fields.Where(field => field.IsSentToDevices)];
        RequiredOnCreateFields = [.. fields.Where(field => field.IsRequiredOnCreate)];
    }

    public SyncDataTypeDefinition Definition { get; }

    public IReadOnlyList<SyncFieldGroup<TRecord>> Groups { get; }

    public IReadOnlyList<SyncField<TRecord>> Fields { get; }

    public IReadOnlyDictionary<string, SyncField<TRecord>> DeviceFields { get; }

    public IReadOnlyList<SyncField<TRecord>> SentFields { get; }

    public IReadOnlyList<SyncField<TRecord>> RequiredOnCreateFields { get; }
}
