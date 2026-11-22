using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

internal abstract class SyncFieldChanges;

internal sealed class SyncFieldChanges<TRecord>(IReadOnlyList<SyncGroupChange<TRecord>> groups) : SyncFieldChanges
    where TRecord : SyncRecord
{
    public IReadOnlyList<SyncGroupChange<TRecord>> Groups { get; } = groups;
}
