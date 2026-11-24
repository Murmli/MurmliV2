using System.Text.Json.Nodes;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal sealed class SyncLoadAccount
{
    private readonly List<Guid> listIds = [];

    private readonly List<List<Guid>> itemIds = [];

    private string token;

    private SyncLoadAccount(SyncLoadDevice writer, string token, int maxCommandsPerRequest)
    {
        Writer = writer;
        this.token = token;
        MaxCommandsPerRequest = maxCommandsPerRequest;
    }

    public SyncLoadDevice Writer { get; }

    public int MaxCommandsPerRequest { get; }

    public IReadOnlyList<Guid> ListIds => listIds;

    public IReadOnlyList<IReadOnlyList<Guid>> ItemIds => itemIds;

    public static async Task<SyncLoadAccount> CreateAsync(HttpClient httpClient, int listCount)
    {
        var writer = await SyncLoadDevice.SignInAsync(httpClient);
        var firstResponse = (await writer.SyncAsync(token: null, [])).ReadResponse();
        var account = new SyncLoadAccount(writer, firstResponse.Token, firstResponse.Settings.MaxCommandsPerRequest);
        account.AddList(Assert.Single(firstResponse.Scopes, scope => scope.Kind == SyncTestDevice.ShoppingListScopeKind).ScopeId);
        var newListIds = Enumerable.Range(0, listCount - 1).Select(_ => Guid.NewGuid()).ToList();
        var now = TimeProvider.System.GetUtcNow();
        await account.ApplyAsync([.. newListIds.Select((listId, index) => SyncLoadCommands.CreateList(listId, now, $"Liste {index + 2}"))]);
        foreach (var listId in newListIds)
        {
            account.AddList(listId);
        }

        return account;
    }

    public Task FillAsync(int itemsPerList, SyncLoadContent content)
    {
        var now = TimeProvider.System.GetUtcNow();
        var commands = new List<JsonObject>();
        for (var listIndex = 0; listIndex < listIds.Count; listIndex++)
        {
            for (var itemIndex = 0; itemIndex < itemsPerList; itemIndex++)
            {
                commands.Add(CreateItem(listIndex, content, now));
            }
        }

        return ApplyAsync(commands);
    }

    public IReadOnlyList<JsonObject>? TakeItemCreates(int count, SyncLoadContent content)
    {
        if (listIds.Count * SyncLoadSettings.MaxItemsPerList - itemIds.Sum(items => items.Count) < count)
        {
            return null;
        }

        var now = TimeProvider.System.GetUtcNow();
        var commands = new List<JsonObject>(count);
        for (var listIndex = 0; commands.Count < count; listIndex++)
        {
            while (itemIds[listIndex].Count < SyncLoadSettings.MaxItemsPerList && commands.Count < count)
            {
                commands.Add(CreateItem(listIndex, content, now));
            }
        }

        return commands;
    }

    public async Task DeleteAllItemsAsync()
    {
        var now = TimeProvider.System.GetUtcNow();
        await ApplyAsync([.. listIds.SelectMany((listId, listIndex) => itemIds[listIndex].Select(itemId => SyncLoadCommands.DeleteItem(listId, itemId, now)))]);
        foreach (var items in itemIds)
        {
            items.Clear();
        }
    }

    public async Task<IReadOnlyList<SyncLoadExchange>> ApplyAsync(IReadOnlyList<JsonObject> commands)
    {
        var exchanges = new List<SyncLoadExchange>();
        var next = 0;
        while (next < commands.Count)
        {
            var batch = commands.Skip(next).Take(MaxCommandsPerRequest).ToList();
            var exchange = await Writer.SyncAsync(token, batch);
            var response = exchange.ReadResponse();
            Assert.NotEmpty(response.Results);
            Assert.Equal(batch.Take(response.Results.Count).Select(command => new SyncCommandResult(SyncTestCommands.GetId(command), "applied")), response.Results);
            token = response.Token;
            next += response.Results.Count;
            exchanges.Add(exchange);
        }

        return exchanges;
    }

    private void AddList(Guid listId)
    {
        listIds.Add(listId);
        itemIds.Add([]);
    }

    private JsonObject CreateItem(int listIndex, SyncLoadContent content, DateTimeOffset time)
    {
        var itemId = Guid.NewGuid();
        itemIds[listIndex].Add(itemId);
        return SyncLoadCommands.CreateItem(listIds[listIndex], itemId, time, content.CreateItemFields());
    }
}
