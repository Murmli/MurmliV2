using System.Globalization;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

// dotnet test shows ITestOutputHelper output only after the test ends, the console shows each line at once
internal static class SyncLoadReport
{
    public static void Write(string scenario, string phase, params IEnumerable<(string Name, double Value)> values) =>
        Console.WriteLine(
            $"SyncLoad {scenario} {phase}: {string.Join(' ', values.Select(value => string.Create(CultureInfo.InvariantCulture, $"{value.Name}={value.Value:0.###}")))}"
        );

    public static void WriteText(string scenario, string phase, params IEnumerable<(string Name, string Value)> values) =>
        Console.WriteLine($"SyncLoad {scenario} {phase}: {string.Join(' ', values.Select(value => $"{value.Name}={value.Value}"))}");

    public static IEnumerable<(string Name, double Value)> Of(SyncLoadRequests requests) =>
    [
        ("requests", requests.Count),
        ("medianMilliseconds", Percentile(requests.Durations, 0.5).TotalMilliseconds),
        ("p95Milliseconds", Percentile(requests.Durations, 0.95).TotalMilliseconds),
        ("maxMilliseconds", requests.Durations.Max().TotalMilliseconds),
        ("wireBytes", requests.First.WireBytes),
        ("bodyBytes", requests.First.Body.Length),
    ];

    public static IEnumerable<(string Name, double Value)> Of(SyncLoadWindow window) =>
    [
        ("windowSeconds", window.Elapsed.TotalSeconds),
        ("cpuShare", window.CpuShare),
        ("cpuMilliseconds", window.CpuMilliseconds),
        ("memoryStartBytes", window.Start.MemoryBytes),
        ("memoryEndBytes", window.End.MemoryBytes),
        ("memoryLimitBytes", window.End.MemoryLimitBytes),
    ];

    public static TimeSpan Percentile(IReadOnlyList<TimeSpan> durations, double fraction)
    {
        var sorted = durations.Order().ToList();
        return sorted[(int)Math.Ceiling(fraction * sorted.Count) - 1];
    }
}
