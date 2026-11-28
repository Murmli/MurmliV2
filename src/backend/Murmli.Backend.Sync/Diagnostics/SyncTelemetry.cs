using System.Diagnostics;
using System.Diagnostics.Metrics;
using Murmli.Backend.Sync.Processing;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Diagnostics;

internal sealed class SyncTelemetry
{
    public const string DeviceWriter = "device";

    public const string ServerWriter = "server";

    public const string CleanupSucceeded = "succeeded";

    public const string CleanupFailed = "failed";

    public const string CleanupSkipped = "skipped";

    public const string CleanupStopped = "stopped";

    public const string TombstonesKind = "tombstones";

    public const string ProcessedCommandsKind = "processed_commands";

    private const string SourceName = "Murmli.Backend.Sync";

    private const string Unknown = "unknown";

    private const string None = "none";

    private const long NoCleanupSuccess = 0;

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private readonly Counter<long> commands;

    private readonly Histogram<long> requestCommands;

    private readonly Histogram<long> pendingCommands;

    private readonly Histogram<long> oldestPendingCommandAge;

    private readonly Histogram<long> responseSize;

    private readonly Histogram<long> compressedResponseSize;

    private readonly Counter<long> fullStateScopes;

    private readonly Counter<long> removedScopes;

    private readonly Counter<long> cleanupRuns;

    private readonly Histogram<double> cleanupDuration;

    private readonly Counter<long> cleanupRemovedRows;

    private long lastCleanupSuccessUnixSeconds;

    public SyncTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(SourceName);
        commands = meter.CreateCounter<long>("murmli.sync.commands", "{command}");
        requestCommands = meter.CreateHistogram<long>("murmli.sync.request.commands", "{command}");
        pendingCommands = meter.CreateHistogram<long>("murmli.sync.device.pending_commands", "{command}");
        oldestPendingCommandAge = meter.CreateHistogram<long>("murmli.sync.device.oldest_pending_age", "s");
        responseSize = meter.CreateHistogram<long>("murmli.sync.response.size", "By");
        compressedResponseSize = meter.CreateHistogram<long>("murmli.sync.response.compressed_size", "By");
        fullStateScopes = meter.CreateCounter<long>("murmli.sync.full_state.scopes", "{scope}");
        removedScopes = meter.CreateCounter<long>("murmli.sync.scopes.removed", "{scope}");
        cleanupRuns = meter.CreateCounter<long>("murmli.sync.cleanup.runs", "{run}");
        cleanupDuration = meter.CreateHistogram<double>("murmli.sync.cleanup.duration", "s");
        cleanupRemovedRows = meter.CreateCounter<long>("murmli.sync.cleanup.removed", "{row}");
        meter.CreateObservableGauge<long>("murmli.sync.cleanup.last_success_time", ObserveLastCleanupSuccess, "s");
    }

    public void RecordRequest(Guid deviceId, SyncRequest request)
    {
        var activity = Activity.Current;
        activity?.SetTag(LogProperties.DeviceId, deviceId.ToString()).SetTag(LogProperties.SyncCommandCount, request.Commands.Count);
        requestCommands.Record(request.Commands.Count);
        if (request.PendingCommandCount is { } pendingCommandCount && pendingCommandCount >= 0)
        {
            pendingCommands.Record(pendingCommandCount);
            activity?.SetTag(LogProperties.SyncPendingCommandCount, pendingCommandCount);
        }

        if (request.OldestPendingCommandAgeSeconds is { } oldestPendingCommandAgeSeconds && oldestPendingCommandAgeSeconds >= 0)
        {
            oldestPendingCommandAge.Record(oldestPendingCommandAgeSeconds);
            activity?.SetTag(LogProperties.SyncOldestPendingCommandAgeSeconds, oldestPendingCommandAgeSeconds);
        }
    }

    public void RecordCommand(string status, string? reasonOrCause, string? kind, string? dataType, string writer) =>
        commands.Add(
            1,
            new TagList
            {
                { "murmli.sync.command.status", status },
                { "murmli.sync.command.reason", reasonOrCause ?? None },
                { "murmli.sync.command.kind", kind ?? Unknown },
                { "murmli.sync.data_type", dataType ?? Unknown },
                { "murmli.sync.writer", writer },
            }
        );

    public void RecordResponse(int processedCommandCount, SyncWrittenResponse written)
    {
        var compressed = written.Response.Encoding == SyncResponseEncoding.Gzip;
        foreach (var reason in written.FullStateReasons)
        {
            fullStateScopes.Add(1, new KeyValuePair<string, object?>("murmli.sync.full_state.reason", reason));
        }

        foreach (var removedScope in written.RemovedScopes)
        {
            removedScopes.Add(1, new KeyValuePair<string, object?>("murmli.sync.removed_by_own_account", removedScope.RemovedByOwnAccount));
        }

        responseSize.Record(written.UncompressedLength, new KeyValuePair<string, object?>("murmli.sync.response.compressed", compressed));
        if (compressed)
        {
            compressedResponseSize.Record(written.Response.Body.Length);
        }

        Activity
            .Current?.SetTag(LogProperties.SyncProcessedCommandCount, processedCommandCount)
            .SetTag(LogProperties.SyncFullStateScopeCount, written.FullStateReasons.Count)
            .SetTag(LogProperties.SyncRemovedScopeCount, written.RemovedScopes.Count)
            .SetTag(LogProperties.SyncResponseBytes, written.UncompressedLength)
            .SetTag(LogProperties.SyncCompressed, compressed);
    }

    public void RecordCleanupRun(string result, TimeSpan duration, IReadOnlyList<(string Kind, int Count)> removedRows, DateTimeOffset runTime)
    {
        cleanupRuns.Add(1, new KeyValuePair<string, object?>("murmli.sync.cleanup.result", result));
        if (result == CleanupSkipped)
        {
            return;
        }

        cleanupDuration.Record(duration.TotalSeconds);
        foreach (var (kind, count) in removedRows)
        {
            cleanupRemovedRows.Add(count, new KeyValuePair<string, object?>("murmli.sync.cleanup.kind", kind));
        }

        if (result == CleanupSucceeded)
        {
            Interlocked.Exchange(ref lastCleanupSuccessUnixSeconds, runTime.ToUnixTimeSeconds());
        }
    }

    private IEnumerable<Measurement<long>> ObserveLastCleanupSuccess()
    {
        var lastCleanupSuccess = Interlocked.Read(ref lastCleanupSuccessUnixSeconds);
        return lastCleanupSuccess == NoCleanupSuccess ? [] : [new Measurement<long>(lastCleanupSuccess)];
    }
}
