using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class WaitingCleanupContributor : ISyncCleanupContributor
{
    public string Name => "test_waiting";

    public Task RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default) =>
        Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
}
