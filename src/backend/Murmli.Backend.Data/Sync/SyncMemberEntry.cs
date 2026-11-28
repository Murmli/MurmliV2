namespace Murmli.Backend.Data.Sync;

public class SyncMemberEntry
{
    public Guid Id { get; set; }

    public Guid ScopeId { get; set; }

    public Guid? AccountId { get; set; }

    public string Name { get; set; } = "";

    public string Role { get; set; } = "";

    public SyncMemberState State { get; set; } = SyncMemberState.Active;

    public DateTimeOffset CreatedAt { get; set; }
}
