using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

internal abstract class SyncFieldChanges;

internal sealed class SyncFieldChanges<TRecord>(
    IReadOnlyList<SyncGroupChange<TRecord>> groups,
    IReadOnlyList<SyncFieldAssignment<TRecord>> createOnlyAssignments,
    IReadOnlyList<SyncFieldAssignment<TRecord>> serverAssignments
) : SyncFieldChanges
    where TRecord : SyncRecord
{
    public IReadOnlyList<SyncGroupChange<TRecord>> Groups { get; } = groups;

    public IReadOnlyList<SyncFieldAssignment<TRecord>> CreateOnlyAssignments { get; } = createOnlyAssignments;

    public IReadOnlyList<SyncFieldAssignment<TRecord>> ServerAssignments { get; } = serverAssignments;
}
