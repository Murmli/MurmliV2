namespace Murmli.Backend.Sync.Diagnostics;

internal static class LogEvents
{
    public const int SyncCommandFailed = 3002;

    public const int SyncDatabaseUnavailable = 3003;

    public const int SyncCleanupCompleted = 3005;

    public const int SyncCleanupSkipped = 3006;

    public const int SyncCleanupFailed = 3007;
}
