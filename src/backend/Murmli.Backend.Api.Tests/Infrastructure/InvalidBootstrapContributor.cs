using System.Text.Json;
using Murmli.Backend.Sync.Accounts;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Writing;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class InvalidBootstrapContributor : ISyncAccountBootstrapContributor
{
    public IReadOnlyList<SyncServerCommand> CreateCommands(Guid accountId)
    {
        var listId = Guid.NewGuid();
        return
        [
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.System,
                SyncCommandKind.Create,
                listId,
                SyncTestDevice.ShoppingListDataType,
                listId,
                new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("") }
            ),
        ];
    }
}
