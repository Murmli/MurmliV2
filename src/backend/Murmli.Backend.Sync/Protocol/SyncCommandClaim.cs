namespace Murmli.Backend.Sync.Protocol;

internal sealed record SyncCommandClaim(Guid CommandId, string Kind, Guid ScopeId, string DataType, Guid RecordId, DateTimeOffset Time);
