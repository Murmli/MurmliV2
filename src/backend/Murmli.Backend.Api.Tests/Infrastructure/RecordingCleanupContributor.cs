using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class RecordingCleanupContributor(ICollection<SyncCleanupContext> contexts) : ISyncCleanupContributor
{
    public Task<int> RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default)
    {
        contexts.Add(context);
        return Task.FromResult(0);
    }
}
