using Microsoft.Extensions.Logging;
using Murmli.Backend.Sync.Diagnostics;

namespace Murmli.Backend.Sync.Processing;

public sealed partial class SyncProcessor
{
    [LoggerMessage(
        EventId = LogEvents.SyncCommandFailed,
        EventName = nameof(LogEvents.SyncCommandFailed),
        Level = LogLevel.Error,
        Message = "Processing the sync command {"
            + LogProperties.CommandId
            + "} of kind {"
            + LogProperties.CommandKind
            + "} for the data type {"
            + LogProperties.DataType
            + "} from the device {"
            + LogProperties.DeviceId
            + "} failed"
    )]
    private partial void LogCommandFailed(Exception exception, Guid? commandId, string? commandKind, string? dataType, Guid deviceId);

    [LoggerMessage(
        EventId = LogEvents.SyncDatabaseUnavailable,
        EventName = nameof(LogEvents.SyncDatabaseUnavailable),
        Level = LogLevel.Warning,
        Message = "The database is unavailable in the sync phase {" + LogProperties.Phase + "}, the request is answered with 503"
    )]
    private partial void LogDatabaseUnavailable(Exception exception, string phase);

    [LoggerMessage(
        EventId = LogEvents.SyncCommandRejected,
        EventName = nameof(LogEvents.SyncCommandRejected),
        Level = LogLevel.Information,
        Message = "The sync command {"
            + LogProperties.CommandId
            + "} of kind {"
            + LogProperties.CommandKind
            + "} for the data type {"
            + LogProperties.DataType
            + "} on the record {"
            + LogProperties.RecordId
            + "} in the scope {"
            + LogProperties.ScopeId
            + "} from the device {"
            + LogProperties.DeviceId
            + "} was rejected with the reason {"
            + LogProperties.Reason
            + "}"
    )]
    private partial void LogCommandRejected(
        Guid deviceId,
        Guid? commandId,
        string? commandKind,
        string? dataType,
        Guid? scopeId,
        Guid? recordId,
        string? reason
    );

    [LoggerMessage(
        EventId = LogEvents.SyncScopeRemoved,
        EventName = nameof(LogEvents.SyncScopeRemoved),
        Level = LogLevel.Information,
        Message = "The sync scope {"
            + LogProperties.ScopeId
            + "} was reported as removed to the device {"
            + LogProperties.DeviceId
            + "}, removed by the own account: {"
            + LogProperties.RemovedByOwnAccount
            + "}"
    )]
    private partial void LogScopeRemoved(Guid deviceId, Guid scopeId, bool removedByOwnAccount);
}
