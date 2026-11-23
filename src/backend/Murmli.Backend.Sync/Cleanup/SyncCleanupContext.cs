namespace Murmli.Backend.Sync.Cleanup;

public sealed record SyncCleanupContext(DateTimeOffset Now, int BatchSize);
