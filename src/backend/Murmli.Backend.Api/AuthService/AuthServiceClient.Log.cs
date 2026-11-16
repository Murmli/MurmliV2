using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Diagnostics;

namespace Murmli.Backend.Api.AuthService;

internal sealed partial class AuthServiceClient
{
    [LoggerMessage(
        EventId = LogEvents.AuthUserDeletionFailed,
        EventName = nameof(LogEvents.AuthUserDeletionFailed),
        Level = LogLevel.Error,
        Message = "Deleting the user in the auth service failed, status code {" + LogProperties.StatusCode + "}"
    )]
    private partial void LogUserDeletionFailed(int? statusCode, Exception? exception = null);
}
