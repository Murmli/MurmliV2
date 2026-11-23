using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Diagnostics;

namespace Murmli.Backend.Api.Users;

internal sealed partial class StaleAccountDeletionContributor
{
    [LoggerMessage(
        EventId = LogEvents.StaleAccountDeletionCompleted,
        EventName = nameof(LogEvents.StaleAccountDeletionCompleted),
        Level = LogLevel.Information,
        Message = "Completed the stale deletion of the user {" + LogProperties.UserId + "}"
    )]
    private partial void LogStaleAccountDeletionCompleted(Guid userId);
}
