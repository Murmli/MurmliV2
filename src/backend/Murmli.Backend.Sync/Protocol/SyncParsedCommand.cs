namespace Murmli.Backend.Sync.Protocol;

internal sealed record SyncParsedCommand(Guid? CommandId, SyncCommandTarget? Target, SyncCommandClaim? Claim, SyncCommand? Command);
