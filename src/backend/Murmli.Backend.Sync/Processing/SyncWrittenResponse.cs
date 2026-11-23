using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

internal sealed record SyncWrittenResponse(
    SyncOutcome.Completed Response,
    long UncompressedLength,
    IReadOnlyList<string> FullStateReasons,
    IReadOnlyList<SyncRemovedScope> RemovedScopes
);
