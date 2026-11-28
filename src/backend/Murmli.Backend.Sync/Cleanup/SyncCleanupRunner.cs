using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Extensions;
using Npgsql;

namespace Murmli.Backend.Sync.Cleanup;

public sealed partial class SyncCleanupRunner(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<SyncOptions> options,
    ILogger<SyncCleanupRunner> logger
)
{
    private static readonly TimeSpan LeaseReserve = TimeSpan.FromMinutes(1);

    private readonly string lockTimeout = options.Value.GetLockTimeoutSetting();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.GetTimestamp();
        var runId = Guid.NewGuid();
        var syncOptions = options.Value;
        var cleanupOptions = syncOptions.Cleanup;
        var now = timeProvider.GetUtcNowInMilliseconds();
        var removed = new RemovedCounts();
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MurmliDbContext>();
        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(cleanupOptions.LeaseDuration - LeaseReserve);
        try
        {
            if (!await TryTakeLeaseAsync(context, runId, now, now + cleanupOptions.LeaseDuration, timeLimit.Token))
            {
                LogCleanupSkipped();
                return;
            }

            await RemoveTombstonesAsync(context, now - syncOptions.Retention, cleanupOptions.BatchSize, removed, timeLimit.Token);
            await RemoveProcessedCommandsAsync(
                context,
                now - syncOptions.Retention - cleanupOptions.SafetyMargin,
                cleanupOptions.BatchSize,
                removed,
                timeLimit.Token
            );
            await RunContributorsAsync(scope.ServiceProvider, new SyncCleanupContext(now, cleanupOptions.BatchSize), removed, timeLimit.Token);
            LogCompleted(start, removed);
        }
        catch (OperationCanceledException) when (timeLimit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The run ends before its lease can expire, the next run removes the rest
            LogStopped(start, removed);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            LogCleanupFailed(exception);
            throw;
        }
        catch (Exception exception)
        {
            // Not rethrown: an exception leaving SyncCleanupService would stop the API host
            LogCleanupFailed(exception);
        }
        finally
        {
            await ReleaseLeaseAsync(context, runId);
        }
    }

    private static async Task<bool> TryTakeLeaseAsync(
        MurmliDbContext context,
        Guid runId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken
    ) =>
        await context
            .Set<CleanupLease>()
            .Where(lease => lease.Singleton && (lease.ExpiresAt == null || lease.ExpiresAt < now || lease.Holder == runId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(lease => lease.Holder, runId).SetProperty(lease => lease.ExpiresAt, expiresAt),
                cancellationToken
            ) == 1;

    private static bool IsLockTimeout(Exception exception) =>
        exception switch
        {
            PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } => true,
            { InnerException: { } innerException } => IsLockTimeout(innerException),
            _ => false,
        };

    private async Task RunContributorsAsync(
        IServiceProvider services,
        SyncCleanupContext cleanupContext,
        RemovedCounts removed,
        CancellationToken cancellationToken
    )
    {
        foreach (var contributor in services.GetServices<ISyncCleanupContributor>())
        {
            try
            {
                removed.Records += await contributor.RunAsync(cleanupContext, cancellationToken);
            }
            catch (Exception exception)
                when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested) && !exception.IsDatabaseDisturbance())
            {
                LogCleanupFailed(exception);
            }
        }
    }

    private async Task RemoveTombstonesAsync(
        MurmliDbContext context,
        DateTimeOffset cutoff,
        int batchSize,
        RemovedCounts removed,
        CancellationToken cancellationToken
    )
    {
        var skippedScopeIds = new List<Guid>();
        while (true)
        {
            var scopeIds = await context
                .Set<SyncTombstone>()
                .Where(tombstone => tombstone.DeletedAt < cutoff && !skippedScopeIds.Contains(tombstone.ScopeId))
                .Select(tombstone => tombstone.ScopeId)
                .Distinct()
                .OrderBy(scopeId => scopeId)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (scopeIds.Count == 0)
            {
                return;
            }

            foreach (var scopeId in scopeIds)
            {
                try
                {
                    removed.Tombstones += await RemoveScopeTombstonesAsync(context, scopeId, cutoff, batchSize, cancellationToken);
                }
                catch (Exception exception) when (IsLockTimeout(exception))
                {
                    skippedScopeIds.Add(scopeId);
                }
            }
        }
    }

    private Task<int> RemoveScopeTombstonesAsync(
        MurmliDbContext context,
        Guid scopeId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken
    ) =>
        context
            .Database.CreateExecutionStrategy()
            .ExecuteInTransactionAsync(
                attemptCancellationToken => RaiseFloorAndDeleteTombstonesAsync(context, scopeId, cutoff, batchSize, attemptCancellationToken),
                // A replay after an ambiguous commit only processes the expired tombstones the first attempt left; the floor never drops
                _ => Task.FromResult(false),
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

    private async Task<int> RaiseFloorAndDeleteTombstonesAsync(
        MurmliDbContext context,
        Guid scopeId,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        context.PrepareSyncUnitAttempt();
        await SetLockTimeoutAsync(context, cancellationToken);
        var floor = await context
            .Set<SyncTombstone>()
            .Where(tombstone => tombstone.ScopeId == scopeId && tombstone.DeletedAt < cutoff)
            .OrderBy(tombstone => tombstone.Version)
            .Take(batchSize)
            .MaxAsync(tombstone => (long?)tombstone.Version, cancellationToken);
        if (floor is not { } tombstoneFloor)
        {
            return 0;
        }

        await context
            .Set<SyncScope>()
            .Where(scope => scope.Id == scopeId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(scope => scope.TombstoneFloor, scope => Math.Max(scope.TombstoneFloor, tombstoneFloor)),
                cancellationToken
            );
        return await context
            .Set<SyncTombstone>()
            .Where(tombstone => tombstone.ScopeId == scopeId && tombstone.DeletedAt < cutoff && tombstone.Version <= tombstoneFloor)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task RemoveProcessedCommandsAsync(
        MurmliDbContext context,
        DateTimeOffset cutoff,
        int batchSize,
        RemovedCounts removed,
        CancellationToken cancellationToken
    )
    {
        int batchCount;
        do
        {
            batchCount = await context
                .Database.CreateExecutionStrategy()
                .ExecuteInTransactionAsync(
                    attemptCancellationToken => DeleteProcessedCommandsAsync(context, cutoff, batchSize, attemptCancellationToken),
                    _ => Task.FromResult(false),
                    IsolationLevel.ReadCommitted,
                    cancellationToken
                );
            removed.ProcessedCommands += batchCount;
        } while (batchCount > 0);
    }

    private async Task<int> DeleteProcessedCommandsAsync(
        MurmliDbContext context,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        context.PrepareSyncUnitAttempt();
        await SetLockTimeoutAsync(context, cancellationToken);
        return await context
            .Set<ProcessedSyncCommand>()
            .Where(command =>
                command.RetainFrom < cutoff
                && !(command.Kind == EF.Constant(SyncCommandKind.Delete.Key) && command.Status == SyncResultStatus.Applied && command.RecordId == command.ScopeId)
            )
            .OrderBy(command => command.RetainFrom)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Task SetLockTimeoutAsync(MurmliDbContext context, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {lockTimeout}, true)", cancellationToken);

    private async Task ReleaseLeaseAsync(MurmliDbContext context, Guid runId)
    {
        try
        {
            await context
                .Set<CleanupLease>()
                .Where(lease => lease.Singleton && lease.Holder == runId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(lease => lease.Holder, (Guid?)null).SetProperty(lease => lease.ExpiresAt, (DateTimeOffset?)null),
                    CancellationToken.None
                );
        }
        catch (Exception exception)
        {
            // The lease expires on its own after Sync:Cleanup:LeaseDuration
            LogCleanupFailed(exception);
        }
    }

    private void LogStopped(long start, RemovedCounts removed) =>
        LogCleanupStopped(
            (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            removed.Tombstones,
            removed.ProcessedCommands,
            removed.Records
        );

    private void LogCompleted(long start, RemovedCounts removed) =>
        LogCleanupCompleted(
            (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            removed.Tombstones,
            removed.ProcessedCommands,
            removed.Records
        );

    private sealed class RemovedCounts
    {
        public int Tombstones { get; set; }

        public int ProcessedCommands { get; set; }

        public int Records { get; set; }
    }
}
