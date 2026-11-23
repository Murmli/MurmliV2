using System.Text.Json;
using Murmli.Backend.Sync.Accounts;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Writing;

namespace Murmli.Backend.Api.ShoppingLists;

internal sealed class FirstShoppingListContributor : ISyncAccountBootstrapContributor
{
    public IReadOnlyList<SyncServerCommand> CreateCommands(Guid accountId)
    {
        var listId = Guid.NewGuid();
        return
        [
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.OnBehalfOf(accountId),
                SyncCommandKind.Create,
                listId,
                ShoppingListType.DataTypeName,
                listId,
                new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("") }
            ),
        ];
    }
}
