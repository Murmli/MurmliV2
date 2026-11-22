using System.Globalization;
using Murmli.Backend.Sync.Configuration;

namespace Murmli.Backend.Sync.Extensions;

internal static class SyncOptionsExtensions
{
    public static string GetLockTimeoutSetting(this SyncOptions options) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)options.LockTimeout.TotalMilliseconds}ms");
}
