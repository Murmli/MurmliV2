using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

internal sealed record SyncCommandUnitInput(
    Guid AccountId,
    SyncCommandClaim Claim,
    DateTimeOffset Now,
    SyncCommand? Command,
    SyncRejectionReason? PreCheckRejection,
    SyncScopeSeed? ScopeSeed
);
