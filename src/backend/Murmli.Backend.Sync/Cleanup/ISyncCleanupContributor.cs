namespace Murmli.Backend.Sync.Cleanup;

public interface ISyncCleanupContributor
{
    string Name { get; }

    Task RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default);
}
