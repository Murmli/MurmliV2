using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class SyncDataTypeLayout<TRecord>
    where TRecord : SyncRecord
{
    public SyncDataTypeLayout(
        SyncDataTypeDefinition definition,
        IReadOnlyList<SyncFieldGroup<TRecord>> groups,
        IReadOnlyList<SyncField<TRecord>> fields,
        IReadOnlyList<SyncField<TRecord>> serverFields
    )
    {
        Definition = definition;
        Groups = groups;
        Fields = fields;
        ServerFields = serverFields;
        var deviceFields = new Dictionary<string, SyncField<TRecord>>(StringComparer.Ordinal);
        foreach (var field in groups.SelectMany(group => group.Fields))
        {
            deviceFields.TryAdd(field.Name, field);
        }

        DeviceFields = deviceFields;
        var writableServerFields = new Dictionary<string, SyncField<TRecord>>(StringComparer.Ordinal);
        foreach (var field in serverFields.Where(field => field.Fill == SyncServerFill.None))
        {
            writableServerFields.TryAdd(field.Name, field);
        }

        WritableServerFields = writableServerFields;
        SentFields = [.. fields.Where(field => field.IsSentToDevices)];
        RequiredOnCreateFields = [.. fields.Where(field => field.IsRequiredOnCreate)];
    }

    public SyncDataTypeDefinition Definition { get; }

    public IReadOnlyList<SyncFieldGroup<TRecord>> Groups { get; }

    public IReadOnlyList<SyncField<TRecord>> Fields { get; }

    public IReadOnlyList<SyncField<TRecord>> ServerFields { get; }

    public IReadOnlyDictionary<string, SyncField<TRecord>> DeviceFields { get; }

    public IReadOnlyDictionary<string, SyncField<TRecord>> WritableServerFields { get; }

    public IReadOnlyList<SyncField<TRecord>> SentFields { get; }

    public IReadOnlyList<SyncField<TRecord>> RequiredOnCreateFields { get; }
}
