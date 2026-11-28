using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class ConstantCleanupContributor : ISyncCleanupContributor
{
    public const int RemovedRecordCount = 3;

    public const string ContributorName = "test_constant";

    public string Name => ContributorName;

    public Task RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default)
    {
        context.AddRemovedRecords(RemovedRecordCount);
        return Task.CompletedTask;
    }
}
