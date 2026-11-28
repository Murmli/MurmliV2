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
using Murmli.Backend.Sync.Diagnostics;
using Murmli.Backend.Sync.Extensions;

namespace Murmli.Backend.Sync.Cleanup;

public sealed partial class SyncCleanupRunner
{
    private const string RunActivityName = "sync cleanup run";

    private static readonly TimeSpan LeaseReserve = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory scopeFactory;

    private readonly TimeProvider timeProvider;

    private readonly IOptions<SyncOptions> options;

    private readonly SyncTelemetry telemetry;

    private readonly ILogger<SyncCleanupRunner> logger;

    private readonly string lockTimeout;

    internal SyncCleanupRunner(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<SyncOptions> options,
        SyncTelemetry telemetry,
        ILogger<SyncCleanupRunner> logger
    )
    {
        this.scopeFactory = scopeFactory;
        this.timeProvider = timeProvider;
        this.options = options;
        this.telemetry = telemetry;
        this.logger = logger;
        lockTimeout = options.Value.GetLockTimeoutSetting();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var activity = SyncTelemetry.ActivitySource.StartActivity(RunActivityName);
        var start = Stopwatch.GetTimestamp();
        var runId = Guid.NewGuid();
        var syncOptions = options.Value;
        var cleanupOptions = syncOptions.Cleanup;
        var now = timeProvider.GetUtcNowInMilliseconds();
        var removed = new RemovedCounts();
        var result = SyncTelemetry.CleanupFailed;
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MurmliDbContext>();
        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(cleanupOptions.LeaseDuration - LeaseReserve);
        try
        {
            if (!await TryTakeLeaseAsync(context, runId, now, now + cleanupOptions.LeaseDuration, timeLimit.Token))
            {
                LogCleanupSkipped();
                result = SyncTelemetry.CleanupSkipped;
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
            await RunContributorsAsync(scope.ServiceProvider, now, cleanupOptions.BatchSize, removed, timeLimit.Token);
            LogCompleted(start, removed);
            result = SyncTelemetry.CleanupSucceeded;
        }
        catch (OperationCanceledException) when (timeLimit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The run ends before its lease can expire, the next run removes the rest
            LogStopped(start, removed);
            result = SyncTelemetry.CleanupStopped;
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
            RecordRun(activity, result, start, removed, now);
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

    private async Task RunContributorsAsync(
        IServiceProvider services,
        DateTimeOffset now,
        int batchSize,
        RemovedCounts removed,
        CancellationToken cancellationToken
    )
    {
        foreach (var contributor in services.GetServices<ISyncCleanupContributor>())
        {
            var cleanupContext = new SyncCleanupContext(now, batchSize);
            try
            {
                await contributor.RunAsync(cleanupContext, cancellationToken);
            }
            catch (Exception exception)
                when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested) && !exception.IsDatabaseDisturbance())
            {
                LogCleanupFailed(exception);
            }
            finally
            {
                removed.Contributors.Add((contributor.Name, cleanupContext.RemovedRecordCount));
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
                catch (Exception exception) when (exception.IsLockTimeout())
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

    private void RecordRun(Activity? activity, string result, long start, RemovedCounts removed, DateTimeOffset runTime)
    {
        telemetry.RecordCleanupRun(
            result,
            Stopwatch.GetElapsedTime(start),
            [(SyncTelemetry.TombstonesKind, removed.Tombstones), (SyncTelemetry.ProcessedCommandsKind, removed.ProcessedCommands), .. removed.Contributors],
            runTime
        );
        if (result == SyncTelemetry.CleanupFailed)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
    }

    private sealed class RemovedCounts
    {
        public int Tombstones { get; set; }

        public int ProcessedCommands { get; set; }

        public List<(string Kind, int Count)> Contributors { get; } = [];

        public int Records => Contributors.Sum(contributor => contributor.Count);
    }
}
