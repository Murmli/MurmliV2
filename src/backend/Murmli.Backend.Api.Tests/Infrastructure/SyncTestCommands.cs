using System.Text.Json.Nodes;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class SyncTestCommands
{
    public static Guid GetId(JsonObject command) => command["id"]!.GetValue<Guid>();

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
