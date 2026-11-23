using System.Globalization;
using OpenTelemetry.Metrics;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class TestMetrics
{
    public static Dictionary<string, long> SumByTags(IEnumerable<MetricSnapshot> snapshots, string name, params string[] tagKeys) =>
        GetPoints(snapshots, name, tagKeys)
            .GroupBy(point => point.Key)
            .ToDictionary(group => group.Key, group => group.Sum(point => point.Point.GetSumLong()));

    private static List<(string Key, MetricPoint Point)> GetPoints(IEnumerable<MetricSnapshot> snapshots, string name, string[] tagKeys)
    {
        var points = new List<(string Key, MetricPoint Point)>();
        foreach (var snapshot in snapshots.Where(snapshot => snapshot.Name == name))
        {
            foreach (var point in snapshot.MetricPoints)
            {
                var tags = new Dictionary<string, string?>();
                foreach (var tag in point.Tags)
                {
                    tags.Add(tag.Key, Convert.ToString(tag.Value, CultureInfo.InvariantCulture));
                }

                Assert.Equal(tagKeys.Order(StringComparer.Ordinal), tags.Keys.Order(StringComparer.Ordinal));
                points.Add((string.Join('|', tagKeys.Select(tagKey => tags[tagKey])), point));
            }
        }

        return points;
    }
}
