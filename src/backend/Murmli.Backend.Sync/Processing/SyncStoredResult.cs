namespace Murmli.Backend.Sync.Processing;

internal sealed record SyncStoredResult(string Status, string? Reason, string? Cause);
