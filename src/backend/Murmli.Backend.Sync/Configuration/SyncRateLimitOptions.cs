using System.ComponentModel.DataAnnotations;

namespace Murmli.Backend.Sync.Configuration;

public sealed class SyncRateLimitOptions
{
    [Range(1, 1_000_000)]
    public int DeviceRequestsPerMinute { get; set; } = 120;

    [Range(1, 1_000_000)]
    public int AccountRequestsPerMinute { get; set; } = 600;

    [Range(1, 100_000_000)]
    public int AccountCommandsPerHour { get; set; } = 2_000;
}
