namespace Murmli.Backend.Sync.Writing;

public abstract record SyncActor
{
    private SyncActor() { }

    public static SyncActor System { get; } = new SystemActor();

    public static SyncActor OnBehalfOf(Guid accountId) => new AccountActor(accountId);

    internal sealed record SystemActor : SyncActor;

    internal sealed record AccountActor(Guid AccountId) : SyncActor;
}
