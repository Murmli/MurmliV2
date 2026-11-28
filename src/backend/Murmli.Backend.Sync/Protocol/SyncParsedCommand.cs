using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Sync.Protocol;

internal sealed record SyncParsedCommand(
    Guid? CommandId,
    SyncCommandKind? Kind,
    SyncDataType? DataType,
    SyncCommandTarget? Target,
    SyncCommandClaim? Claim,
    SyncCommand? Command
);
