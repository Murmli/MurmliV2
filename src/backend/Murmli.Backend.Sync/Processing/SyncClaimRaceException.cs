namespace Murmli.Backend.Sync.Processing;

internal sealed class SyncClaimRaceException()
    : Exception("The processed sync command was removed between its claim and the read of its stored result.");
