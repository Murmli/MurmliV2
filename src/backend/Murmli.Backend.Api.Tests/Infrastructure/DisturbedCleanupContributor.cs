using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class DisturbedCleanupContributor : ISyncCleanupContributor
{
    public string Name => "test_disturbed";

    public Task RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default) => throw new TimeoutException();
}
