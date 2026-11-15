using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Diagnostics;

namespace Murmli.Backend.Api.Users;

internal sealed partial class UserRegistry
{
    [LoggerMessage(
        EventId = LogEvents.UserCreated,
        EventName = nameof(LogEvents.UserCreated),
        Level = LogLevel.Information,
        Message = "Created the user {" + LogProperties.UserId + "}"
    )]
    private partial void LogUserCreated(Guid userId);
}
