using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Data.ShoppingLists;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.Protocol;
using OpenTelemetry.Logs;

namespace Murmli.Backend.Api.Tests.ShoppingLists;

public class CheckedItemsCleanupContributorTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string CompletedEvent = "SyncCleanupCompleted";

    private const string FailedEvent = "SyncCleanupFailed";

    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static readonly TimeSpan PastRetention = Retention + TimeSpan.FromMilliseconds(1);

    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    private readonly List<LogRecord> logRecords = [];

    [Fact]
    public async Task RunAsync_ItemCheckedLongerThanRetention_RemovedOnEveryDevice()
    {
        await using var api = await StartApiAsync();
        var phone = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var checkedItemId = Guid.NewGuid();
        var uncheckedItemId = Guid.NewGuid();
        var listId = await CreateListAsync(phone, now, checkedItemId);
        await ApplyAsync(phone, phone.CreateItem(listId, uncheckedItemId, now, SyncTestDevice.ItemFields("Brot")));
        var tablet = phone.OtherDevice();
        await tablet.SyncShoppingAsync();
        api.TimeProvider.Advance(PastRetention);

        await RunCleanupAsync(api);

        Assert.All(
            [await phone.SyncShoppingAsync(), await tablet.SyncShoppingAsync()],
            response =>
            {
                var block = Assert.Single(response.Scopes, block => block.ScopeId == listId);
                Assert.False(block.FullState);
                Assert.Empty(block.Records);
                Assert.Equal([new SyncDeletedRecord(SyncTestDevice.ShoppingListItemDataType, checkedItemId)], block.DeletedRecords);
            }
        );
        Assert.Null(await ShoppingListQueries.ReadItemAsync(api, checkedItemId));
        Assert.NotNull(await ShoppingListQueries.ReadItemAsync(api, uncheckedItemId));
        Assert.Equal([1], ReadRemovedRecordCounts());
        Assert.DoesNotContain(logRecords, logRecord => logRecord.EventId.Name == FailedEvent);
    }

    [Theory]
    [InlineData("checkedExactlyRetentionAgo")]
    [InlineData("uncheckedBeforeRun")]
    [InlineData("checkedLongAgoByDeviceClock")]
    public async Task RunAsync_ItemNotCheckedLongEnough_Stays(string scenario)
    {
        await using var api = await StartApiAsync();
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var itemId = Guid.NewGuid();
        var listId = await CreateListAsync(device, now);
        switch (scenario)
        {
            case "checkedExactlyRetentionAgo":
                await ApplyAsync(device, device.CreateItem(listId, itemId, now, CheckedItemFields()));
                api.TimeProvider.Advance(Retention);
                break;
            case "uncheckedBeforeRun":
                await ApplyAsync(device, device.CreateItem(listId, itemId, now, CheckedItemFields()));
                api.TimeProvider.Advance(TimeSpan.FromSeconds(1));
                await ApplyAsync(device, device.SetItemFields(listId, itemId, now.AddSeconds(1), new JsonObject { ["checked"] = false }));
                api.TimeProvider.Advance(TimeSpan.FromDays(31));
                break;
            case "checkedLongAgoByDeviceClock":
                await ApplyAsync(device, device.CreateItem(listId, itemId, now.AddDays(-41), SyncTestDevice.ItemFields("Milch")));
                await ApplyAsync(device, device.SetItemFields(listId, itemId, now.AddDays(-40), new JsonObject { ["checked"] = true }));
                api.TimeProvider.Advance(TimeSpan.FromDays(29));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        await RunCleanupAsync(api);

        Assert.NotNull(await ShoppingListQueries.ReadItemAsync(api, itemId));
        Assert.Equal([0], ReadRemovedRecordCounts());
    }

    [Fact]
    public async Task RunAsync_UncheckCommittedWhileRemovalWaits_KeepsItem()
    {
        await using var api = await StartApiAsync(new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:30" });
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var itemId = Guid.NewGuid();
        var listId = await CreateListAsync(device, now, itemId);
        api.TimeProvider.Advance(PastRetention);
        var administration = new DatabaseAdministration(connectionString);
        var uncheck = device.SetItemFields(listId, itemId, now, new JsonObject { ["checked"] = false });
        Task<SyncResponse> uncheckResponse;
        Task run;

        await using (await administration.LockScopeRowAsync(listId))
        {
            uncheckResponse = device.SyncShoppingAsync(uncheck);
            await administration.WaitForLockWaitersAsync(1);
            run = RunCleanupAsync(api);
            await administration.WaitForLockWaitersAsync(2);
        }

        await run;
        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(uncheck), "applied"), Assert.Single((await uncheckResponse).Results));
        var item = Assert.IsType<ShoppingListItem>(await ShoppingListQueries.ReadItemAsync(api, itemId));
        Assert.Equal((false, (DateTimeOffset?)null), (item.Checked, item.CheckedArrivedAt));
        Assert.Equal([0], ReadRemovedRecordCounts());
    }

    [Fact]
    public async Task RunAsync_RecheckCommittedWhileRemovalWaits_KeepsItem()
    {
        await using var api = await StartApiAsync(new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:30" });
        var phone = await SyncTestDevice.SignInAsync(api);
        var tablet = phone.OtherDevice();
        var now = api.TimeProvider.GetUtcNow();
        var itemId = Guid.NewGuid();
        var listId = await CreateListAsync(phone, now, itemId);
        api.TimeProvider.Advance(PastRetention);
        var recheckArrival = api.TimeProvider.GetUtcNow();
        var administration = new DatabaseAdministration(connectionString);
        var uncheck = phone.SetItemFields(listId, itemId, now.AddSeconds(1), new JsonObject { ["checked"] = false });
        var recheck = tablet.SetItemFields(listId, itemId, now.AddSeconds(2), new JsonObject { ["checked"] = true });
        Task<SyncResponse> uncheckResponse;
        Task<SyncResponse> recheckResponse;
        Task run;

        await using (await administration.LockScopeRowAsync(listId))
        {
            uncheckResponse = phone.SyncShoppingAsync(uncheck);
            await administration.WaitForLockWaitersAsync(1);
            recheckResponse = tablet.SyncShoppingAsync(recheck);
            await administration.WaitForLockWaitersAsync(2);
            run = RunCleanupAsync(api);
            await administration.WaitForLockWaitersAsync(3);
        }

        await run;
        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(uncheck), "applied"), Assert.Single((await uncheckResponse).Results));
        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(recheck), "applied"), Assert.Single((await recheckResponse).Results));
        var item = Assert.IsType<ShoppingListItem>(await ShoppingListQueries.ReadItemAsync(api, itemId));
        Assert.Equal((true, (DateTimeOffset?)recheckArrival), (item.Checked, item.CheckedArrivedAt));
        Assert.Equal([0], ReadRemovedRecordCounts());
    }

    [Fact]
    public async Task RunAsync_ListLockedPastLockTimeout_SkipsListUntilNextRun()
    {
        await using var api = await StartApiAsync(
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" }
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        Guid[] lockedItemIds = [Guid.NewGuid(), Guid.NewGuid()];
        var otherItemId = Guid.NewGuid();
        var lockedListId = await CreateListAsync(device, now, lockedItemIds);
        await CreateListAsync(device, now, otherItemId);
        api.TimeProvider.Advance(PastRetention);

        await using (await new DatabaseAdministration(connectionString).LockScopeRowAsync(lockedListId))
        {
            await RunCleanupAsync(api);
        }

        var lockedItemsAfterLockedRun = await ShoppingListQueries.CountItemsAsync(api, lockedListId);
        var otherItemAfterLockedRun = await ShoppingListQueries.ReadItemAsync(api, otherItemId);
        var removedCountsAfterLockedRun = ReadRemovedRecordCounts();
        await RunCleanupAsync(api);

        Assert.Equal(2, lockedItemsAfterLockedRun);
        Assert.Null(otherItemAfterLockedRun);
        Assert.Equal([1], removedCountsAfterLockedRun);
        Assert.Equal(0, await ShoppingListQueries.CountItemsAsync(api, lockedListId));
        Assert.Equal([1, 2], ReadRemovedRecordCounts());
        Assert.DoesNotContain(logRecords, logRecord => logRecord.EventId.Name == FailedEvent);
    }

    [Fact]
    public async Task RunAsync_MoreItemsThanBatchSize_RemovesAllInOneRun()
    {
        await using var api = await StartApiAsync(new Dictionary<string, string?> { ["Sync:Cleanup:BatchSize"] = "2" });
        var device = await SyncTestDevice.SignInAsync(api);
        var listId = await CreateListAsync(device, api.TimeProvider.GetUtcNow(), [.. Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())]);
        api.TimeProvider.Advance(PastRetention);

        await RunCleanupAsync(api);

        Assert.Equal(0, await ShoppingListQueries.CountItemsAsync(api, listId));
        Assert.Equal([5], ReadRemovedRecordCounts());
    }

    [Fact]
    public async Task RunAsync_CheckedItemRetentionConfigured_RemovesAfterConfiguredDuration()
    {
        await using var api = await StartApiAsync(new Dictionary<string, string?> { ["ShoppingLists:CheckedItemRetention"] = "01:00:00" });
        var device = await SyncTestDevice.SignInAsync(api);
        var itemId = Guid.NewGuid();
        await CreateListAsync(device, api.TimeProvider.GetUtcNow(), itemId);
        api.TimeProvider.Advance(TimeSpan.FromHours(1));

        await RunCleanupAsync(api);
        var itemAfterRetention = await ShoppingListQueries.ReadItemAsync(api, itemId);
        api.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await RunCleanupAsync(api);

        Assert.NotNull(itemAfterRetention);
        Assert.Null(await ShoppingListQueries.ReadItemAsync(api, itemId));
    }

    private static Task RunCleanupAsync(ApiHost api) =>
        api.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(TestContext.Current.CancellationToken);

    private static async Task ApplyAsync(SyncTestDevice device, params JsonObject[] commands)
    {
        var response = await device.SyncShoppingAsync(commands);
        Assert.Equal(commands.Select(command => new SyncCommandResult(SyncTestCommands.GetId(command), "applied")), response.Results);
    }

    private static async Task<Guid> CreateListAsync(SyncTestDevice device, DateTimeOffset now, params Guid[] checkedItemIds)
    {
        var listId = Guid.NewGuid();
        await ApplyAsync(
            device,
            [device.CreateList(listId, now.AddSeconds(-60), "Einkauf"), .. checkedItemIds.Select(itemId => device.CreateItem(listId, itemId, now, CheckedItemFields()))]
        );
        return listId;
    }

    private static JsonObject CheckedItemFields()
    {
        var fields = SyncTestDevice.ItemFields("Milch");
        fields["checked"] = true;
        return fields;
    }

    private Task<ApiHost> StartApiAsync(IReadOnlyDictionary<string, string?>? configuration = null) =>
        ApiHost.StartAsync(connectionString, configuration, logRecords: logRecords);

    private int[] ReadRemovedRecordCounts() =>
        [
            .. logRecords
                .Where(logRecord => logRecord.EventId.Name == CompletedEvent)
                .Select(logRecord => Assert.IsType<int>(logRecord.Attributes!.ToDictionary()["RemovedRecordCount"])),
        ];
}
