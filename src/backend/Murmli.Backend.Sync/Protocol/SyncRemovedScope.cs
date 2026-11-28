namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncRemovedScope(Guid ScopeId, bool RemovedByOwnAccount);
