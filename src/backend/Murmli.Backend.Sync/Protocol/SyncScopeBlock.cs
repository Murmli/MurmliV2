namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncScopeBlock(
    Guid ScopeId,
    string Kind,
    bool FullState,
    IReadOnlyList<SyncRecordEntry> Records,
    IReadOnlyList<SyncDeletedRecord> DeletedRecords,
    IReadOnlyList<SyncMember> Members
);
