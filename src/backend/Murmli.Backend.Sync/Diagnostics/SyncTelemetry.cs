using System.Diagnostics;
using System.Diagnostics.Metrics;
using Murmli.Backend.Sync.Processing;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Diagnostics;

internal sealed class SyncTelemetry
{
    public const string DeviceWriter = "device";

    public const string ServerWriter = "server";

    private const string SourceName = "Murmli.Backend.Sync";

    private const string Unknown = "unknown";

    private const string None = "none";

    private readonly Counter<long> commands;

    private readonly Histogram<long> requestCommands;

    private readonly Histogram<long> pendingCommands;

    private readonly Histogram<long> oldestPendingCommandAge;

    private readonly Histogram<long> responseSize;

    private readonly Histogram<long> compressedResponseSize;

    private readonly Counter<long> fullStateScopes;

    private readonly Counter<long> removedScopes;

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
}
