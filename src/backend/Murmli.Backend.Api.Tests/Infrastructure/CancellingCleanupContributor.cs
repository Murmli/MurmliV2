using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class CancellingCleanupContributor(CancellationTokenSource runCancellation) : ISyncCleanupContributor
{
    public const int RemovedRecordCount = 2;

    public string Name => "test_cancelling";

    public async Task RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default)
    {
        context.AddRemovedRecords(RemovedRecordCount);
        await runCancellation.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
