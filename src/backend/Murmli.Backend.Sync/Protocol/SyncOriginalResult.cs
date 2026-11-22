namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncOriginalResult(string Status, string? Reason = null, string? Cause = null);
