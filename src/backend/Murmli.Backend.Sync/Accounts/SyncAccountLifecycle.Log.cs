using Microsoft.Extensions.Logging;
using Murmli.Backend.Sync.Diagnostics;

namespace Murmli.Backend.Sync.Accounts;

public sealed partial class SyncAccountLifecycle
{
    [LoggerMessage(
        EventId = LogEvents.SyncAccountBootstrapped,
        EventName = nameof(LogEvents.SyncAccountBootstrapped),
        Level = LogLevel.Information,
        Message = "The first sync of the account created its personal sync scope {" + LogProperties.ScopeId + "}"
    )]
    private partial void LogAccountBootstrapped(Guid scopeId);
}
