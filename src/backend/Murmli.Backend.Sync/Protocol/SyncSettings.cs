namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncSettings(int PollIntervalSeconds, int MaxCommandsPerRequest, int MaxRequestBytes);
