using System.Text.Json;
using System.Text.Json.Nodes;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Data.ShoppingLists;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Protocol;
using Murmli.Backend.Sync.Writing;

namespace Murmli.Backend.Api.Tests.ShoppingLists;

public class ShoppingListItemTypeTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    private static readonly SyncServerWriteResult AppliedResult = new("applied", null, null);

    [Fact]
    public async Task Sync_ItemWithEveryField_RoundTripsEveryFieldExceptCreator()
    {
        var (device, listId, ownerEntryId, now) = await SignInWithListAsync();
        var reader = device.OtherDevice();
        var itemId = Guid.NewGuid();
        var created = now.AddSeconds(-30);
        var checkedTime = now.AddSeconds(-20);
        await ApplyAsync(
            device,
            device.CreateItem(
                listId,
                itemId,
                created,
                new JsonObject
                {
                    ["name"] = "Hafermilch",
                    ["quantity"] = 1.5m,
                    ["unit"] = "liter",
                    ["category"] = "dairy",
                    ["checked"] = false,
                    ["language"] = "de",
                }
            )
        );

        var initial = await reader.SyncShoppingAsync();
        var initialItem = await ReadItemAsync(itemId);
        await ApplyAsync(device, device.SetItemFields(listId, itemId, checkedTime, new JsonObject { ["checked"] = true }));
        var afterCheck = await reader.SyncShoppingAsync();
        var checkedItem = await ReadItemAsync(itemId);

        var initialRecord = Assert.Single(Assert.Single(initial.Scopes, block => block.ScopeId == listId).Records, record => record.Id == itemId);
        Assert.Equal(ExpectedFields(isChecked: false, changedAt: created), initialRecord.Fields.GetRawText());
        Assert.Equal(
            ((Guid?)ownerEntryId, (Guid?)ownerEntryId, created, created, (DateTimeOffset?)null),
            (
                initialItem.CreatedByMemberId,
                initialItem.ChangedByMemberId,
                initialItem.CreatedAt,
                initialItem.ChangedAt,
                initialItem.CheckedArrivedAt
            )
        );
        var checkedBlock = Assert.Single(afterCheck.Scopes, block => block.ScopeId == listId);
        Assert.False(checkedBlock.FullState);
        var checkedRecord = Assert.Single(checkedBlock.Records, record => record.Id == itemId);
        Assert.Equal(ExpectedFields(isChecked: true, changedAt: checkedTime), checkedRecord.Fields.GetRawText());
        Assert.Equal(now, checkedItem.CheckedArrivedAt);
        Assert.All(
            device.ResponseBodies.Concat(reader.ResponseBodies),
            body => Assert.DoesNotContain("\"createdBy\"", body, StringComparison.Ordinal)
        );

        string ExpectedFields(bool isChecked, DateTimeOffset changedAt) =>
            $$"""{"name":"Hafermilch","quantity":1.5,"unit":"liter","category":"dairy","checked":{{JsonSerializer.Serialize(isChecked)}},"language":"de","createdAt":{{created.ToUnixTimeMilliseconds()}},"changedAt":{{changedAt.ToUnixTimeMilliseconds()}},"changedBy":"{{ownerEntryId}}"}""";
    }

    [Fact]
    public async Task Sync_CreateOnExistingItem_KeepsLanguageAndCreation()
    {
        var (device, listId, ownerEntryId, now) = await SignInWithListAsync();
        var itemId = Guid.NewGuid();
        var created = now.AddSeconds(-30);
        var recreated = now.AddSeconds(-20);
        await ApplyAsync(device, device.CreateItem(listId, itemId, created, SyncTestDevice.ItemFields("Milch")));

        await ApplyAsync(
            device,
            device.CreateItem(
                listId,
                itemId,
                recreated,
                new JsonObject
                {
                    ["name"] = "Vollmilch",
                    ["category"] = "beverages",
                    ["language"] = "en",
                    ["checked"] = true,
                }
            )
        );

        var item = await ReadItemAsync(itemId);
        Assert.Equal(
            ("Vollmilch", "beverages", true, "de", created, recreated, (Guid?)ownerEntryId),
            (item.Name, item.Category, item.Checked, item.Language, item.CreatedAt, item.ChangedAt, item.CreatedByMemberId)
        );
    }

    [Theory]
    [InlineData("nameEmpty")]
    [InlineData("nameTooLong")]
    [InlineData("quantityZero")]
    [InlineData("quantityTooLarge")]
    [InlineData("quantityTooPrecise")]
    [InlineData("amountPartial")]
    [InlineData("unitUnknown")]
    [InlineData("categoryUnknown")]
    [InlineData("categoryNull")]
    [InlineData("categoryMissing")]
    [InlineData("languageMissing")]
    [InlineData("languageUpperCase")]
    [InlineData("languageTooShort")]
    [InlineData("languageTooLong")]
    [InlineData("languageWithNewline")]
    [InlineData("checkedAsString")]
    [InlineData("createdAtSent")]
    [InlineData("changedAtSent")]
    [InlineData("changedBySent")]
    [InlineData("createdBySent")]
    [InlineData("languageOnSetFields")]
    public async Task Sync_InvalidItemCommand_RejectedInvalid(string scenario)
    {
        var (device, listId, ownerEntryId, now) = await SignInWithListAsync();
        var existingItemId = Guid.NewGuid();
        await ApplyAsync(device, device.CreateItem(listId, existingItemId, now.AddSeconds(-30), SyncTestDevice.ItemFields("Milch")));
        var existingItem = await ReadItemAsync(existingItemId);
        var newItemId = Guid.NewGuid();
        var command = scenario switch
        {
            "nameEmpty" => CreateNewItem(("name", "")),
            "nameTooLong" => CreateNewItem(("name", new string('a', 257))),
            "quantityZero" => CreateNewItem(("quantity", 0), ("unit", null)),
            "quantityTooLarge" => CreateNewItem(("quantity", 1_000_000), ("unit", null)),
            "quantityTooPrecise" => CreateNewItem(("quantity", 1.2345m), ("unit", null)),
            "amountPartial" => CreateNewItem(("quantity", 1)),
            "unitUnknown" => CreateNewItem(("quantity", 1), ("unit", "cup")),
            "categoryUnknown" => CreateNewItem(("category", "toys")),
            "categoryNull" => CreateNewItem(("category", null)),
            "categoryMissing" => CreateNewItemWithout("category"),
            "languageMissing" => CreateNewItemWithout("language"),
            "languageUpperCase" => CreateNewItem(("language", "DE")),
            "languageTooShort" => CreateNewItem(("language", "d")),
            "languageTooLong" => CreateNewItem(("language", "deut")),
            "languageWithNewline" => CreateNewItem(("language", "de\n")),
            "checkedAsString" => CreateNewItem(("checked", "true")),
            "createdAtSent" => CreateNewItem(("createdAt", 1)),
            "changedAtSent" => CreateNewItem(("changedAt", 1)),
            "changedBySent" => CreateNewItem(("changedBy", ownerEntryId)),
            "createdBySent" => CreateNewItem(("createdBy", ownerEntryId)),
            "languageOnSetFields" => device.SetItemFields(listId, existingItemId, now, new JsonObject { ["language"] = "en", ["name"] = "Vollmilch" }),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        var response = await device.SyncShoppingAsync(command);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), "rejected", "invalid"), Assert.Single(response.Results));
        if (scenario == "languageOnSetFields")
        {
            var item = await ReadItemAsync(existingItemId);
            Assert.Equal(("de", "Milch", existingItem.ChangedAt), (item.Language, item.Name, item.ChangedAt));
        }
        else
        {
            Assert.Null(await ShoppingListQueries.ReadItemAsync(fixture.Api, newItemId));
        }

        JsonObject CreateNewItem(params (string Name, JsonNode? Value)[] changes)
        {
            var fields = SyncTestDevice.ItemFields("Milch");
            foreach (var (name, value) in changes)
            {
                fields[name] = value;
            }

            return device.CreateItem(listId, newItemId, now, fields);
        }

        JsonObject CreateNewItemWithout(string name)
        {
            var fields = SyncTestDevice.ItemFields("Milch");
            fields.Remove(name);
            return device.CreateItem(listId, newItemId, now, fields);
        }
    }

    [Theory]
    [InlineData("nameOf256")]
    [InlineData("quantityMaximum")]
    [InlineData("quantitySmallest")]
    [InlineData("unitWithoutQuantity")]
    [InlineData("languageOfThreeLetters")]
    public async Task Sync_ItemBoundaryValue_Applied(string scenario)
    {
        var (device, listId, _, now) = await SignInWithListAsync();
        var itemId = Guid.NewGuid();
        (string Name, decimal? Quantity, string? Unit, string Language) expected = scenario switch
        {
            "nameOf256" => (new string('a', 256), null, null, "de"),
            "quantityMaximum" => ("Milch", 999_999.999m, "gram", "de"),
            "quantitySmallest" => ("Milch", 0.001m, "kilogram", "de"),
            "unitWithoutQuantity" => ("Milch", null, "piece", "de"),
            "languageOfThreeLetters" => ("Milch", null, null, "deu"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
        var fields = SyncTestDevice.ItemFields(expected.Name, language: expected.Language);
        if (expected.Quantity is not null || expected.Unit is not null)
        {
            fields["quantity"] = expected.Quantity;
            fields["unit"] = expected.Unit;
        }

        await ApplyAsync(device, device.CreateItem(listId, itemId, now, fields));

        var item = await ReadItemAsync(itemId);
        Assert.Equal(expected, (item.Name, item.Quantity, item.Unit, item.Language));
    }

    [Fact]
    public async Task Sync_EveryCatalogKey_Accepted()
    {
        var catalog = await ShoppingCatalogFile.ReadAsync();
        var (device, listId, _, now) = await SignInWithListAsync();
        (Guid ItemId, string Field, string Key, JsonObject Fields)[] items =
        [
            .. catalog.Categories.Select(category => (Guid.NewGuid(), "category", category, SyncTestDevice.ItemFields(category, category))),
            .. catalog.Units.Select(unit =>
                (
                    Guid.NewGuid(),
                    "unit",
                    unit,
                    new JsonObject
                    {
                        ["name"] = unit,
                        ["category"] = "miscellaneous",
                        ["quantity"] = 1,
                        ["unit"] = unit,
                        ["language"] = "de",
                    }
                )
            ),
        ];

        var response = await device.SyncShoppingAsync([.. items.Select(item => device.CreateItem(listId, item.ItemId, now, item.Fields))]);
        var readerResponse = await device.OtherDevice().SyncShoppingAsync();

        Assert.Equal(22, catalog.Categories.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(11, catalog.Units.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Repeat("applied", 33), response.Results.Select(result => result.Status));
        var records = Assert.Single(readerResponse.Scopes, block => block.ScopeId == listId).Records;
        Assert.All(
            items,
            item =>
                Assert.Equal(item.Key, Assert.Single(records, record => record.Id == item.ItemId).Fields.GetProperty(item.Field).GetString())
        );
    }

    [Fact]
    public async Task Sync_CheckedChanges_MaintainCheckedArrivedAtByServerTime()
    {
        var (device, listId, _, now) = await SignInWithListAsync();
        var itemId = Guid.NewGuid();

        await ApplyAsync(device, device.CreateItem(listId, itemId, now.AddDays(-11), SyncTestDevice.ItemFields("Milch")));
        var afterCreate = await ReadCheckedArrivedAtAsync(itemId);
        await ApplyAsync(device, device.SetItemFields(listId, itemId, now.AddDays(-10), new JsonObject { ["checked"] = true }));
        var afterCheck = await ReadCheckedArrivedAtAsync(itemId);
        fixture.Api.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        var minuteLater = fixture.Api.TimeProvider.GetUtcNow();
        await ApplyAsync(device, device.SetItemFields(listId, itemId, now.AddDays(-9), new JsonObject { ["checked"] = true }));
        var afterRepeatedCheck = await ReadCheckedArrivedAtAsync(itemId);
        await ApplyAsync(device, device.SetItemFields(listId, itemId, now.AddDays(-8), new JsonObject { ["quantity"] = 2, ["unit"] = "liter" }));
        var afterAmountChange = await ReadCheckedArrivedAtAsync(itemId);
        await ApplyAsync(device, device.SetItemFields(listId, itemId, now.AddDays(-7), new JsonObject { ["checked"] = false }));
        var afterUncheck = await ReadCheckedArrivedAtAsync(itemId);
        var checkedItemId = Guid.NewGuid();
        var checkedFields = SyncTestDevice.ItemFields("Brot");
        checkedFields["checked"] = true;
        await ApplyAsync(device, device.CreateItem(listId, checkedItemId, minuteLater, checkedFields));
        var afterCheckedCreate = await ReadCheckedArrivedAtAsync(checkedItemId);

        Assert.Equal(
            new DateTimeOffset?[] { null, now, now, now, null, minuteLater },
            new[] { afterCreate, afterCheck, afterRepeatedCheck, afterAmountChange, afterUncheck, afterCheckedCreate }
        );
    }

    [Fact]
    public async Task WriteAsync_SystemUpdateOfItem_KeepsChangedAtAndChangedBy()
    {
        var (device, listId, ownerEntryId, now) = await SignInWithListAsync();
        var itemId = Guid.NewGuid();
        var created = now.AddSeconds(-30);
        await ApplyAsync(device, device.CreateItem(listId, itemId, created, SyncTestDevice.ItemFields("Milch")));

        var systemResult = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.System,
                SyncCommandKind.SetFields,
                listId,
                SyncTestDevice.ShoppingListItemDataType,
                itemId,
                SyncServerFields.Of(("checked", true))
            )
        );
        var afterSystemWrite = await ReadItemAsync(itemId);
        var onBehalfResult = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.OnBehalfOf(device.AccountId),
                SyncCommandKind.SetFields,
                listId,
                SyncTestDevice.ShoppingListItemDataType,
                itemId,
                SyncServerFields.Of(("name", "Vollmilch"))
            )
        );
        var afterOnBehalfWrite = await ReadItemAsync(itemId);

        Assert.Equal(AppliedResult, systemResult);
        Assert.Equal(
            (true, now, (DateTimeOffset?)now, created, (Guid?)ownerEntryId),
            (
                afterSystemWrite.Checked,
                afterSystemWrite.CheckedChangedAt,
                afterSystemWrite.CheckedArrivedAt,
                afterSystemWrite.ChangedAt,
                afterSystemWrite.ChangedByMemberId
            )
        );
        Assert.Equal(AppliedResult, onBehalfResult);
        Assert.Equal(
            ("Vollmilch", now, (Guid?)ownerEntryId),
            (afterOnBehalfWrite.Name, afterOnBehalfWrite.ChangedAt, afterOnBehalfWrite.ChangedByMemberId)
        );
    }

    [Fact]
    public async Task WriteAsync_SystemCreateOfItem_FillsTimesWithoutPerson()
    {
        var (device, listId, ownerEntryId, now) = await SignInWithListAsync();
        var itemId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.System,
                SyncCommandKind.Create,
                listId,
                SyncTestDevice.ShoppingListItemDataType,
                itemId,
                SyncServerFields.Of(("name", "Brot"), ("category", "bakery"), ("language", "de"))
            )
        );
        var item = await ReadItemAsync(itemId);
        var response = await device.SyncShoppingAsync();
        var onBehalfResult = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.OnBehalfOf(device.AccountId),
                SyncCommandKind.SetFields,
                listId,
                SyncTestDevice.ShoppingListItemDataType,
                itemId,
                SyncServerFields.Of(("checked", true))
            )
        );
        var afterOnBehalfWrite = await ReadItemAsync(itemId);

        Assert.Equal(AppliedResult, result);
        Assert.Equal((now, now, (Guid?)null, (Guid?)null), (item.CreatedAt, item.ChangedAt, item.ChangedByMemberId, item.CreatedByMemberId));
        var record = Assert.Single(Assert.Single(response.Scopes, block => block.ScopeId == listId).Records, record => record.Id == itemId);
        Assert.Equal(
            (JsonValueKind.Null, now.ToUnixTimeMilliseconds()),
            (record.Fields.GetProperty("changedBy").ValueKind, record.Fields.GetProperty("createdAt").GetInt64())
        );
        Assert.Equal(AppliedResult, onBehalfResult);
        Assert.Equal(
            (true, (Guid?)ownerEntryId, (Guid?)null),
            (afterOnBehalfWrite.Checked, afterOnBehalfWrite.ChangedByMemberId, afterOnBehalfWrite.CreatedByMemberId)
        );
    }

    private static async Task ApplyAsync(SyncTestDevice device, JsonObject command)
    {
        var response = await device.SyncShoppingAsync(command);
        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), "applied"), Assert.Single(response.Results));
    }

    private async Task<(SyncTestDevice Device, Guid ListId, Guid OwnerEntryId, DateTimeOffset Now)> SignInWithListAsync()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var listId = Guid.NewGuid();
        await ApplyAsync(device, device.CreateList(listId, now.AddSeconds(-60), "Einkauf"));
        var ownerEntryId = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, listId)).Id;
        return (device, listId, ownerEntryId, now);
    }

    private async Task<ShoppingListItem> ReadItemAsync(Guid itemId) =>
        Assert.IsType<ShoppingListItem>(await ShoppingListQueries.ReadItemAsync(fixture.Api, itemId));

    private async Task<DateTimeOffset?> ReadCheckedArrivedAtAsync(Guid itemId) => (await ReadItemAsync(itemId)).CheckedArrivedAt;
}
