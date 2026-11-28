namespace Murmli.Backend.Sync.Processing;

internal sealed record SyncScopeHead(
    Guid Id,
    string Kind,
    long Version,
    int Epoch,
    long TombstoneFloor,
    Guid DatabaseIdentity,
    bool IsAccountBeingDeleted
);
