using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Sync.Protocol;

internal sealed record SyncCommand(SyncCommandClaim Claim, SyncCommandKind Kind, SyncDataType DataType, SyncFieldChanges? Changes);
