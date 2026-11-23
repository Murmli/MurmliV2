using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class ConstantCleanupContributor : ISyncCleanupContributor
{
    public const int RemovedRecordCount = 3;

    public Task<int> RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(RemovedRecordCount);
}
