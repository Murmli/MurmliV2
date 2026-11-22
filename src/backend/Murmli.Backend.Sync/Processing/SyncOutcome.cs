namespace Murmli.Backend.Sync.Processing;

public abstract record SyncOutcome
{
    private SyncOutcome() { }

    public sealed record Completed(ReadOnlyMemory<byte> Body) : SyncOutcome;

    public sealed record AccountGone : SyncOutcome;

    public sealed record DatabaseUnavailable : SyncOutcome;
}
