using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.Extensions;

internal static class MurmliDbContextExtensions
{
    public static void PrepareSyncUnitAttempt(this MurmliDbContext context)
    {
        context.ChangeTracker.Clear();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        context.Database.AutoSavepointsEnabled = false;
    }

    public static async Task<SyncScope?> LockPersonalScopeAsync(
        this MurmliDbContext context,
        Guid accountId,
        CancellationToken cancellationToken = default
    ) =>
        (
            await context
                .Set<SyncScope>()
                .FromSql($"SELECT * FROM sync.scopes WHERE owner_account_id = {accountId} AND kind = 'personal' FOR NO KEY UPDATE")
                .ToListAsync(cancellationToken)
        ).SingleOrDefault();
}
