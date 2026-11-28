namespace Murmli.Backend.Data.Sync;

public class SyncTombstone
{
    public string DataType { get; set; } = "";

    public Guid RecordId { get; set; }

    public Guid ScopeId { get; set; }

    public long Version { get; set; }

    public DateTimeOffset DeletedAt { get; set; }
}
