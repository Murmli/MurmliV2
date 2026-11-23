using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Diagnostics;
using Murmli.Backend.Data;
using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Users;

internal sealed partial class StaleAccountDeletionContributor(
    MurmliDbContext context,
    UserRegistry userRegistry,
    ILogger<StaleAccountDeletionContributor> logger
) : ISyncCleanupContributor
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public async Task<int> RunAsync(SyncCleanupContext cleanupContext, CancellationToken cancellationToken = default)
    {
        var markedBefore = cleanupContext.Now - StaleAfter;
        var accounts = await context
            .Users.Where(user => user.DeletionStartedAt < markedBefore)
            .OrderBy(user => user.DeletionStartedAt)
            .ThenBy(user => user.Id)
            .Select(user => new { user.Id, user.AuthUserId })
            .Take(cleanupContext.BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (logger.BeginScope(new Dictionary<string, object> { [LogProperties.UserId] = account.Id }))
            {
                if (await userRegistry.CompleteDeletionAsync(account.AuthUserId, CancellationToken.None))
                {
                    LogStaleAccountDeletionCompleted(account.Id);
                }
            }
        }

        // Records removed with an account's whole scopes are not counted as removed records of the run
        return 0;
    }
}
