namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncMember(Guid MemberId, string Name, string Role, string State, bool IsOwn);
