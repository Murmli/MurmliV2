namespace Murmli.Backend.Sync.Cleanup;

public sealed class SyncCleanupContext(DateTimeOffset now, int batchSize)
{
    public DateTimeOffset Now { get; } = now;

    public int BatchSize { get; } = batchSize;

    internal int RemovedRecordCount { get; private set; }

    public void AddRemovedRecords(int count) => RemovedRecordCount += count;
}
