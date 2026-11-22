namespace Murmli.Backend.Sync.Processing;

public abstract record SyncOutcome
{
    private SyncOutcome() { }

    public sealed record Completed(ReadOnlyMemory<byte> Body, SyncResponseEncoding Encoding) : SyncOutcome;

    public sealed record AccountGone : SyncOutcome;

    public sealed record DatabaseUnavailable : SyncOutcome;
}
