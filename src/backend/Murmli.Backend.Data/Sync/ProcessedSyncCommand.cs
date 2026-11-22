namespace Murmli.Backend.Data.Sync;

public class ProcessedSyncCommand
{
    public Guid AccountId { get; set; }

    public Guid CommandId { get; set; }

    public string Kind { get; set; } = "";

    public Guid ScopeId { get; set; }

    public string DataType { get; set; } = "";

    public Guid RecordId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }

    public DateTimeOffset RetainFrom { get; set; }

    public SyncResultStatus? Status { get; set; }

    public string? Reason { get; set; }

    public string? Cause { get; set; }
}
