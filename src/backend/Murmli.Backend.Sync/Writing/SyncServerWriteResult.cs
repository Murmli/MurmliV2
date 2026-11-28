namespace Murmli.Backend.Sync.Writing;

public sealed record SyncServerWriteResult(string Status, string? Reason, string? Cause);
