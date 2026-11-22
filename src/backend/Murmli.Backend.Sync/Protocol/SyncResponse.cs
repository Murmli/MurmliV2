namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncResponse(
    IReadOnlyList<SyncCommandResult> Results,
    IReadOnlyList<SyncScopeBlock> Scopes,
    IReadOnlyList<SyncRemovedScope> RemovedScopes,
    bool CoversAllScopes,
    string Token,
    bool HasMore,
    long ServerTime,
    SyncSettings Settings
);
