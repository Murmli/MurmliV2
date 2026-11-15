using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Diagnostics;

namespace Murmli.Backend.Api.Zitadel;

internal sealed partial class ZitadelUserClient
{
    [LoggerMessage(
        EventId = LogEvents.ZitadelUserDeletionFailed,
        EventName = nameof(LogEvents.ZitadelUserDeletionFailed),
        Level = LogLevel.Error,
        Message = "Deleting the user in Zitadel failed, status code {" + LogProperties.StatusCode + "}"
    )]
    private partial void LogUserDeletionFailed(int? statusCode, Exception? exception = null);
}
