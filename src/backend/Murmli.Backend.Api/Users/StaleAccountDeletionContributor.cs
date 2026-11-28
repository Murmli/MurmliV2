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
        StaleAccount? previous = null;
        while (await ReadStaleAccountsAsync(markedBefore, previous, cleanupContext.BatchSize, cancellationToken) is { Count: > 0 } accounts)
        {
            foreach (var account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (logger.BeginScope(new Dictionary<string, object> { [LogProperties.UserId] = account.Id }))
                {
                    // A started deletion runs to its end; the run stops only between accounts, inside its lease reserve
                    if (await userRegistry.CompleteDeletionAsync(account.AuthUserId, CancellationToken.None))
                    {
                        LogStaleAccountDeletionCompleted(account.Id);
                    }
                }
            }

            previous = accounts[^1];
        }

        // Records removed with an account's whole scopes are not counted as removed records of the run
        return 0;
    }

    private Task<List<StaleAccount>> ReadStaleAccountsAsync(
        DateTimeOffset markedBefore,
        StaleAccount? previous,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        var accounts = context.Users.Where(user => user.DeletionStartedAt < markedBefore);
        if (previous is not null)
        {
            // The marker never changes once set, so each account comes at most once per run
            accounts = accounts.Where(user =>
                EF.Functions.GreaterThan(ValueTuple.Create(user.DeletionStartedAt, user.Id), ValueTuple.Create(previous.DeletionStartedAt, previous.Id))
            );
        }

        return accounts
            .OrderBy(user => user.DeletionStartedAt)
            .ThenBy(user => user.Id)
            .Select(user => new StaleAccount(user.Id, user.AuthUserId, user.DeletionStartedAt))
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private sealed record StaleAccount(Guid Id, string AuthUserId, DateTimeOffset? DeletionStartedAt);
}
