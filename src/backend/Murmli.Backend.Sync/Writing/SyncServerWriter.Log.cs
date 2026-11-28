using Microsoft.Extensions.Logging;
using Murmli.Backend.Sync.Diagnostics;

namespace Murmli.Backend.Sync.Writing;

public sealed partial class SyncServerWriter
{
    [LoggerMessage(
        EventId = LogEvents.SyncCommandFailed,
        EventName = nameof(LogEvents.SyncCommandFailed),
        Level = LogLevel.Error,
        Message = "Writing the sync command {"
            + LogProperties.CommandId
            + "} of kind {"
            + LogProperties.CommandKind
            + "} for the data type {"
            + LogProperties.DataType
            + "} as the server failed"
    )]
    private partial void LogCommandFailed(Exception exception, Guid commandId, string commandKind, string dataType);
}
