using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class ThrowingCleanupContributor : ISyncCleanupContributor
{
    public const string FailureMessage = "The cleanup contributor failed on purpose.";

    public Task<int> RunAsync(SyncCleanupContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(FailureMessage);
}
