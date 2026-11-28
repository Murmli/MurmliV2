namespace Murmli.Backend.Data.Sync;

public class CleanupLease
{
    public bool Singleton { get; set; }

    public Guid? Holder { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}
