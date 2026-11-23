using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace Murmli.Backend.Sync.Configuration;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    [Range(1, 10_000)]
    public int MaxCommandsPerRequest { get; set; } = 500;

    [Range(1_000, 100_000_000)]
    public int MaxRequestBytes { get; set; } = 1_000_000;

    [Range(1, 86_400)]
    public int PollIntervalSeconds { get; set; } = 60;

    [Range(typeof(TimeSpan), "00:01:00", "3650.00:00:00")]
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(45);

    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan ClockAheadTolerance { get; set; } = TimeSpan.FromMinutes(5);

    [Range(typeof(TimeSpan), "00:00:00", "00:00:55")]
    public TimeSpan CommandPhaseBudget { get; set; } = TimeSpan.FromSeconds(20);

    [Range(typeof(TimeSpan), "00:00:01", "00:00:55")]
    public TimeSpan RequestDeadline { get; set; } = TimeSpan.FromSeconds(45);

    [Range(typeof(TimeSpan), "00:00:00.001", "00:00:30")]
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    [EnumDataType(typeof(CompressionLevel))]
    public CompressionLevel FullStateCompressionLevel { get; set; } = CompressionLevel.Fastest;

    public Dictionary<string, SyncDataTypeOptions> DataTypes { get; set; } = [];

    [ValidateObjectMembers]
    public SyncCleanupOptions Cleanup { get; set; } = new();
}
