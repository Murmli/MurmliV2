namespace Murmli.Backend.Sync.Processing;

internal sealed record SyncScopeSeed(Guid OwnerMemberId, int Epoch);
