namespace Murmli.Backend.Sync.Protocol;

internal readonly record struct SyncTokenScope(Guid ScopeId, int Epoch, long Version);
