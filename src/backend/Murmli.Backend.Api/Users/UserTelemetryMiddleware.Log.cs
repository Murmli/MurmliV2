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

    [LoggerMessage(
        EventId = LogEvents.AccountResolutionFailed,
        EventName = nameof(LogEvents.AccountResolutionFailed),
        Level = LogLevel.Error,
        Message = "Resolving the account of the request failed, the request is answered with 500"
    )]
    private partial void LogAccountResolutionFailed(Exception exception);
}
