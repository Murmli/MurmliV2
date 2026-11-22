namespace Murmli.Backend.Data.Sync;

public abstract class SyncRecord
{
    public Guid Id { get; init; }

    public Guid ScopeId { get; init; }

    public long Version { get; set; }
}
