using System.Text.Json.Nodes;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal static class SyncLoadCommands
{
    public static JsonObject CreateList(Guid listId, DateTimeOffset time, string name) =>
        SyncTestCommands.Create("create", SyncTestDevice.ShoppingListDataType, listId, listId, time, new JsonObject { ["name"] = name });

    public static JsonObject CreateItem(Guid listId, Guid itemId, DateTimeOffset time, JsonObject fields) =>
        SyncTestCommands.Create("create", SyncTestDevice.ShoppingListItemDataType, listId, itemId, time, fields);

    public static JsonObject SetItemName(Guid listId, Guid itemId, DateTimeOffset time, string name) =>
        SyncTestCommands.Create("setFields", SyncTestDevice.ShoppingListItemDataType, listId, itemId, time, new JsonObject { ["name"] = name });

    public static JsonObject DeleteItem(Guid listId, Guid itemId, DateTimeOffset time) =>
        SyncTestCommands.Create("delete", SyncTestDevice.ShoppingListItemDataType, listId, itemId, time, fields: null);
}
