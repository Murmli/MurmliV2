using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Diagnostics;

namespace Murmli.Backend.Api.Users;

internal sealed partial class UserTelemetryMiddleware
{
    [LoggerMessage(
        EventId = LogEvents.DatabaseUnavailable,
        EventName = nameof(LogEvents.DatabaseUnavailable),
        Level = LogLevel.Warning,
        Message = "The database is unavailable, the request is answered with 503"
    )]
    private partial void LogDatabaseUnavailable(Exception exception);
}
