using System.Diagnostics;
using System.Globalization;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class TestTelemetry
{
    public const string UserIdProperty = "UserId";

    public static string CreateTraceParent(ActivityTraceId traceId) =>
        $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01";

    public static List<KeyValuePair<string, object?>> GetScopeItems(LogRecord logRecord)
    {
        var items = new List<KeyValuePair<string, object?>>();
        logRecord.ForEachScope(
            static (scope, scopeItems) =>
            {
                foreach (var item in scope)
                {
                    scopeItems.Add(item);
                }
            },
            items
        );
        return items;
    }

    public static object? FindUserId(LogRecord logRecord) => GetScopeItems(logRecord).SingleOrDefault(item => item.Key == UserIdProperty).Value;

    public static string Describe(Activity activity) =>
        string.Join(
            '\n',
            [
                activity.DisplayName,
                activity.StatusDescription,
                .. activity.TagObjects.Select(Describe),
                .. activity.Events.SelectMany(activityEvent => activityEvent.Tags).Select(Describe),
            ]
        );

    public static string Describe(LogRecord logRecord) =>
        string.Join(
            '\n',
            [
                logRecord.FormattedMessage,
                .. (logRecord.Attributes ?? []).Select(Describe),
                .. GetScopeItems(logRecord).Select(Describe),
                logRecord.Exception?.ToString(),
            ]
        );

    public static string Describe(MetricSnapshot snapshot)
    {
        var lines = new List<string> { snapshot.Name };
        foreach (var point in snapshot.MetricPoints)
        {
            foreach (var tag in point.Tags)
            {
                lines.Add(Describe(tag));
            }
        }

        return string.Join('\n', lines);
    }

    public static string Describe(KeyValuePair<string, object?> item) =>
        $"{item.Key}={Convert.ToString(item.Value, CultureInfo.InvariantCulture)}";
}
