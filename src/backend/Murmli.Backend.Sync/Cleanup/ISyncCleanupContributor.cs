namespace Murmli.Backend.Sync.Cleanup;

public interface ISyncCleanupContributor
{
    Task<int> RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default);
}
