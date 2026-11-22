using System.Diagnostics;
using OpenTelemetry.Logs;

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
}
