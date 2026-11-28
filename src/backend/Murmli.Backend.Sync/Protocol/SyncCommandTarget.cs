using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Sync.Protocol;

internal sealed record SyncCommandTarget(Guid ScopeId, string DataType, Guid RecordId, bool IsRootDelete)
{
    public static SyncCommandTarget FromCommand(Guid scopeId, string dataType, Guid recordId, string? kind) =>
        new(scopeId, dataType, recordId, kind == SyncCommandKind.Delete.Key && recordId == scopeId);
}
