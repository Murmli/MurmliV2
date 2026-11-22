using System.ComponentModel.DataAnnotations;

namespace Murmli.Backend.Api.Database;

internal sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 2;

    [Range(typeof(TimeSpan), "00:00:00", "00:00:30")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan UnavailableRetryAfter { get; set; } = TimeSpan.FromSeconds(10);
}
