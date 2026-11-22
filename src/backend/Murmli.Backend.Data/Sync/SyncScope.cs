namespace Murmli.Backend.Data.Sync;

public class SyncScope
{
    public const string PersonalKind = "personal";

    public Guid Id { get; set; }

    public string Kind { get; set; } = "";

    public Guid OwnerAccountId { get; set; }

    public long Version { get; set; }

    public int Epoch { get; set; }

    public long TombstoneFloor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
