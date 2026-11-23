using System.Text.Json.Nodes;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.ShoppingLists;

public class ShoppingListTypeTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    [Theory]
    [InlineData(0, "applied", null)]
    [InlineData(100, "applied", null)]
    [InlineData(101, "rejected", "invalid")]
    public async Task Sync_CreateListWithName_DecidesByLength(int nameLength, string expectedStatus, string? expectedReason)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var listId = Guid.NewGuid();
        var name = new string('a', nameLength);
        var create = device.CreateList(listId, fixture.Api.TimeProvider.GetUtcNow(), name);

        var response = await device.SyncShoppingAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), expectedStatus, expectedReason), Assert.Single(response.Results));
        if (expectedStatus == "applied")
        {
            var block = Assert.Single(response.Scopes, block => block.ScopeId == listId);
            Assert.True(block.FullState);
            var record = Assert.Single(block.Records);
            Assert.Equal(
                (SyncTestDevice.ShoppingListDataType, listId, $$"""{"name":"{{name}}"}"""),
                (record.DataType, record.Id, record.Fields.GetRawText())
            );
            var member = Assert.Single(block.Members);
            Assert.Equal(new SyncMember(member.MemberId, "", "owner", "active", true), member);
        }
        else
        {
            Assert.Null(await ShoppingListQueries.ReadListAsync(fixture.Api, listId));
            Assert.Null(await SharedScopeQueries.ReadScopeAsync(fixture.Api, listId));
        }
    }

    [Fact]
    public async Task Sync_CreateListWithoutName_RejectedInvalid()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var listId = Guid.NewGuid();
        var create = device.CreateCommand(
            "create",
            SyncTestDevice.ShoppingListDataType,
            listId,
            listId,
            fixture.Api.TimeProvider.GetUtcNow(),
            new JsonObject()
        );

        var response = await device.SyncShoppingAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "rejected", "invalid"), Assert.Single(response.Results));
        Assert.Null(await ShoppingListQueries.ReadListAsync(fixture.Api, listId));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(fixture.Api, listId));
    }
}
