using Microsoft.Extensions.Logging;
using Murmli.Backend.Sync.Diagnostics;

namespace Murmli.Backend.Sync.Cleanup;

public sealed partial class SyncCleanupRunner
{
    [LoggerMessage(
        EventId = LogEvents.SyncCleanupCompleted,
        EventName = nameof(LogEvents.SyncCleanupCompleted),
        Level = LogLevel.Information,
        Message = "The sync cleanup run removed {"
            + LogProperties.RemovedTombstoneCount
            + "} tombstones, {"
            + LogProperties.RemovedProcessedCommandCount
            + "} processed commands and {"
            + LogProperties.RemovedRecordCount
            + "} records in {"
            + LogProperties.DurationMilliseconds
            + "} ms"
    )]
    private partial void LogCleanupCompleted(
        long durationMilliseconds,
        int removedTombstoneCount,
        int removedProcessedCommandCount,
        int removedRecordCount
    );

    [LoggerMessage(
        EventId = LogEvents.SyncCleanupStopped,
        EventName = nameof(LogEvents.SyncCleanupStopped),
        Level = LogLevel.Warning,
        Message = "The sync cleanup run stopped at its time limit after removing {"
            + LogProperties.RemovedTombstoneCount
            + "} tombstones, {"
            + LogProperties.RemovedProcessedCommandCount
            + "} processed commands and {"
            + LogProperties.RemovedRecordCount
            + "} records in {"
            + LogProperties.DurationMilliseconds
            + "} ms"
    )]
    private partial void LogCleanupStopped(
        long durationMilliseconds,
        int removedTombstoneCount,
        int removedProcessedCommandCount,
        int removedRecordCount
    );

    [LoggerMessage(
        EventId = LogEvents.SyncCleanupSkipped,
        EventName = nameof(LogEvents.SyncCleanupSkipped),
        Level = LogLevel.Information,
        Message = "The sync cleanup run was skipped because another run holds the lease"
    )]
    private partial void LogCleanupSkipped();

    [LoggerMessage(
        EventId = LogEvents.SyncCleanupFailed,
        EventName = nameof(LogEvents.SyncCleanupFailed),
        Level = LogLevel.Error,
        Message = "A step of the sync cleanup run failed"
    )]
    private partial void LogCleanupFailed(Exception exception);
}
