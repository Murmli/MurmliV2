using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed record SyncFieldAssignment<TRecord>(SyncField<TRecord> Field, Action<TRecord> Apply)
    where TRecord : SyncRecord;
