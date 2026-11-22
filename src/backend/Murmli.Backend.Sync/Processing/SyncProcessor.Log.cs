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
}
