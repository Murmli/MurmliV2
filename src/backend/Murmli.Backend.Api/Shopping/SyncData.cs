using System.Globalization;
using System.Text.Json;

namespace Murmli.Backend.Api.Shopping;

internal sealed class SyncData(IReadOnlyDictionary<string, JsonElement>? data)
{
    public bool Contains(string column) => data?.ContainsKey(column) == true;

    public string GetRequiredString(string column) =>
        FindString(column) is { Length: > 0 } value ? value : throw new SyncValueException($"{column} is required");

    public string? FindString(string column) =>
        Find(column) switch
        {
            null => null,
            { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => throw new SyncValueException($"{column} must be a string"),
        };

    public double? FindDouble(string column) =>
        Find(column) switch
        {
            null => null,
            { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
            _ => throw new SyncValueException($"{column} must be a number"),
        };

    public bool? FindBoolean(string column) =>
        Find(column) switch
        {
            null => null,
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) && number is 0 or 1 => number == 1,
            _ => throw new SyncValueException($"{column} must be 0, 1 or a boolean"),
        };

    public DateTimeOffset? FindTimestamp(string column)
    {
        var value = FindString(column);
        if (value is null)
        {
            return null;
        }

        // Npgsql only writes timestamptz values with offset zero
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            ? timestamp.ToUniversalTime()
            : throw new SyncValueException($"{column} must be an ISO-8601 timestamp");
    }

    public DateTimeOffset GetRequiredTimestamp(string column) => FindTimestamp(column) ?? throw new SyncValueException($"{column} is required");

    public Guid GetRequiredGuid(string column) =>
        Guid.TryParse(FindString(column), out var value) ? value : throw new SyncValueException($"{column} must be a uuid");

    private JsonElement? Find(string column) =>
        data is not null && data.TryGetValue(column, out var element) && element.ValueKind != JsonValueKind.Null ? element : null;
}
