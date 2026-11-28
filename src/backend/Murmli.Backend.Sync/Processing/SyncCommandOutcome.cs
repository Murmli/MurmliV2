using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

internal sealed record SyncCommandOutcome(
    Guid? CommandId,
    SyncResultStatus Status,
    string? Reason,
    string? Cause,
    SyncStoredResult? Original,
    SyncCommandTarget? Target
);
