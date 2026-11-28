namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncCommandResult(
    Guid? CommandId,
    string Status,
    string? Reason = null,
    string? Cause = null,
    SyncOriginalResult? Original = null
);
