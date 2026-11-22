using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data;

namespace Murmli.Backend.Sync.Extensions;

internal static class MurmliDbContextExtensions
{
    public static void PrepareSyncUnitAttempt(this MurmliDbContext context)
    {
        context.ChangeTracker.Clear();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        context.Database.AutoSavepointsEnabled = false;
    }
}
