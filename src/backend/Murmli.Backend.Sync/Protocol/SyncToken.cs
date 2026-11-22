namespace Murmli.Backend.Sync.Protocol;

internal sealed record SyncToken(Guid DatabaseIdentity, IReadOnlyList<SyncTokenScope> Scopes);
