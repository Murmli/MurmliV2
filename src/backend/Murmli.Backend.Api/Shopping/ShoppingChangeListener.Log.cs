using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Diagnostics;

namespace Murmli.Backend.Api.Shopping;

internal sealed partial class ShoppingChangeListener
{
    [LoggerMessage(
        EventId = LogEvents.ShoppingChangeListenerFailed,
        EventName = nameof(LogEvents.ShoppingChangeListenerFailed),
        Level = LogLevel.Warning,
        Message = "Listening for shopping changes failed, reconnecting"
    )]
    private partial void LogListenerFailed(Exception exception);

    [LoggerMessage(
        EventId = LogEvents.InvalidShoppingChangeNotification,
        EventName = nameof(LogEvents.InvalidShoppingChangeNotification),
        Level = LogLevel.Warning,
        Message = "Received an invalid shopping change notification"
    )]
    private partial void LogInvalidNotification(Exception exception);
}
