using System.Text.Json.Nodes;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class SyncTestCommands
{
    public static Guid GetId(JsonObject command) => command["id"]!.GetValue<Guid>();

    public static JsonObject Create(string kind, string dataType, Guid scopeId, Guid recordId, DateTimeOffset time, JsonObject? fields)
    {
        var command = new JsonObject
        {
            ["id"] = Guid.NewGuid(),
            ["kind"] = kind,
            ["scopeId"] = scopeId,
            ["dataType"] = dataType,
            ["recordId"] = recordId,
            ["time"] = time.ToUnixTimeMilliseconds(),
        };
        if (fields is not null)
        {
            command["fields"] = fields;
        }

        return command;
    }

    public static JsonObject CreateBody(string? token, IReadOnlyList<string> dataTypes, IReadOnlyList<JsonObject> commands) =>
        new()
        {
            ["token"] = token,
            ["dataTypes"] = new JsonArray([.. dataTypes.Select(dataType => JsonValue.Create(dataType))]),
            ["commands"] = new JsonArray([.. commands.Select(command => command.DeepClone())]),
        };

    public static JsonObject TitleFields(string title) => new() { ["title"] = title };

    public static JsonObject With(JsonObject command, string member, JsonNode? value)
    {
        command[member] = value;
        return command;
    }

    public static JsonObject Without(JsonObject command, string member)
    {
        command.Remove(member);
        return command;
    }

    public static JsonObject WithField(JsonObject command, string field, JsonNode? value)
    {
        command["fields"]![field] = value;
        return command;
    }
}
