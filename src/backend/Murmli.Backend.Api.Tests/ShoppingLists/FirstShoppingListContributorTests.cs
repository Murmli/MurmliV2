using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.ShoppingLists;

public class FirstShoppingListContributorTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    [Fact]
    public async Task Sync_FirstSync_ReturnsOneUnnamedListWithOwnEntry()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api, firstSync: false);

        var response = await device.SyncShoppingAsync();

        Assert.Equal(2, response.Scopes.Count);
        Assert.Equal(SyncScope.PersonalKind, response.Scopes[0].Kind);
        var block = response.Scopes[1];
        Assert.Equal(SyncTestDevice.ShoppingListScopeKind, block.Kind);
        Assert.True(block.FullState);
        var record = Assert.Single(block.Records);
        Assert.Equal(
            (SyncTestDevice.ShoppingListDataType, block.ScopeId, """{"name":""}"""),
            (record.DataType, record.Id, record.Fields.GetRawText())
        );
        var entry = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, block.ScopeId));
        Assert.Equal(new SyncMember(entry.Id, "", "owner", "active", true), Assert.Single(block.Members));
        Assert.True(response.CoversAllScopes);
        Assert.Equal([block.ScopeId], await ShoppingListQueries.ReadOwnedListIdsAsync(fixture.Api, device.AccountId));
        var processedCommand = Assert.Single(
            await fixture.Api.QueryDatabaseAsync(context =>
                context
                    .Set<ProcessedSyncCommand>()
                    .AsNoTracking()
                    .Where(command => command.AccountId == device.AccountId)
                    .ToListAsync(TestContext.Current.CancellationToken)
            )
        );
        Assert.Equal(
            (SyncTestDevice.ShoppingListDataType, "create", block.ScopeId, SyncResultStatus.Applied),
            (processedCommand.DataType, processedCommand.Kind, processedCommand.RecordId, processedCommand.Status)
        );
    }

    [Fact]
    public async Task Sync_FirstListDeleted_NoListAfterLaterSyncs()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api, firstSync: false);
        var firstListId = Assert
            .Single((await device.SyncShoppingAsync()).Scopes, block => block.Kind == SyncTestDevice.ShoppingListScopeKind)
            .ScopeId;
        var delete = device.DeleteList(firstListId, fixture.Api.TimeProvider.GetUtcNow());
        Assert.Equal(
            new SyncCommandResult(SyncTestCommands.GetId(delete), "applied"),
            Assert.Single((await device.SyncShoppingAsync(delete)).Results)
        );

        var response = await device.OtherDevice().SyncShoppingAsync();

        Assert.DoesNotContain(response.Scopes, block => block.Kind == SyncTestDevice.ShoppingListScopeKind);
        Assert.Empty(await ShoppingListQueries.ReadOwnedListIdsAsync(fixture.Api, device.AccountId));
    }

    [Fact]
    public async Task Sync_FirstSyncWithoutShoppingTypes_CreatesListSeenLater()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api, firstSync: false);

        var first = await device.SyncAsync();

        Assert.Equal(SyncScope.PersonalKind, Assert.Single(first.Scopes).Kind);
        var firstListId = Assert.Single(await ShoppingListQueries.ReadOwnedListIdsAsync(fixture.Api, device.AccountId));
        var block = Assert.Single((await device.OtherDevice().SyncShoppingAsync()).Scopes, block => block.ScopeId == firstListId);
        var record = Assert.Single(block.Records);
        Assert.Equal(
            (SyncTestDevice.ShoppingListDataType, firstListId, """{"name":""}"""),
            (record.DataType, record.Id, record.Fields.GetRawText())
        );
    }
}
