namespace Murmli.Backend.Sync.Protocol;

internal sealed record SyncCommandTarget(Guid ScopeId, string DataType, Guid RecordId);
