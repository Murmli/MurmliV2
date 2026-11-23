namespace Murmli.Backend.Sync.Diagnostics;

internal static class LogEvents
{
    public const int SyncCommandRejected = 3000;

    public const int SyncScopeRemoved = 3001;

    public const int SyncCommandFailed = 3002;

    public const int SyncDatabaseUnavailable = 3003;

    public const int SyncAccountBootstrapped = 3004;

    public const int SyncCleanupCompleted = 3005;

    public const int SyncCleanupSkipped = 3006;

    public const int SyncCleanupFailed = 3007;

    public const int SyncCleanupStopped = 3008;
}
