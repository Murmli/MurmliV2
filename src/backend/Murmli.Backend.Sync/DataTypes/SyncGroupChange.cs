using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed record SyncGroupChange<TRecord>(SyncFieldGroup<TRecord> Group, IReadOnlyList<Action<TRecord>> Assignments)
    where TRecord : SyncRecord;
