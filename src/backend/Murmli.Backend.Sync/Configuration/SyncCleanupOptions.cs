using System.ComponentModel.DataAnnotations;

namespace Murmli.Backend.Sync.Configuration;

public sealed class SyncCleanupOptions
{
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    [Range(1, 100_000)]
    public int BatchSize { get; set; } = 1_000;

    [Range(typeof(TimeSpan), "00:00:00", "30.00:00:00")]
    public TimeSpan SafetyMargin { get; set; } = TimeSpan.FromDays(1);

    [Range(typeof(TimeSpan), "00:02:00", "1.00:00:00")]
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(10);
}
