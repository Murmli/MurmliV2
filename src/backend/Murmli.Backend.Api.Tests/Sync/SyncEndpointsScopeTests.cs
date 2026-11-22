using System.Text.Json.Nodes;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsScopeTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    [Fact]
    public async Task Sync_RootCommandWithRecordIdOtherThanScopeId_RejectedInvalid()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var boardId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var create = device.CreateCommand(
            "create",
            TestBoardType.DataTypeName,
            boardId,
            recordId,
            fixture.Api.TimeProvider.GetUtcNow(),
            new JsonObject { ["title"] = "board" }
        );

        var response = await device.SyncAllAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "rejected", "invalid"), Assert.Single(response.Results));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(fixture.Api, recordId));
    }

    [Theory]
    [InlineData("createCard")]
    [InlineData("setCardFields")]
    [InlineData("deleteCard")]
    [InlineData("setBoardTitle")]
    [InlineData("deleteBoard")]
    [InlineData("createBoardWithSameId")]
    public async Task Sync_CommandFromOtherAccount_RejectedNoAccessAndChangesNothing(string commandName)
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await CreateBoardWithCardAsync(owner, boardId, cardId, now);
        var versionBefore = (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))?.Version;
        var other = await SyncTestDevice.SignInAsync(fixture.Api);
        var command = commandName switch
        {
            "createCard" => other.CreateCard(boardId, Guid.NewGuid(), now, "changed"),
            "setCardFields" => other.SetCardFields(boardId, cardId, now, new JsonObject { ["text"] = "changed" }),
            "deleteCard" => other.DeleteCard(boardId, cardId, now),
            "setBoardTitle" => other.SetBoardTitle(boardId, now, "changed"),
            "deleteBoard" => other.DeleteBoard(boardId, now),
            "createBoardWithSameId" => other.CreateBoard(boardId, now, "mine"),
            _ => throw new ArgumentOutOfRangeException(nameof(commandName), commandName, null),
        };

        var response = await other.SyncAllAsync(command);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), "rejected", "noAccess"), Assert.Single(response.Results));
        Assert.Equal(versionBefore, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))?.Version);
        Assert.Equal("milk", (await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId))?.Text);
        Assert.Equal("board", await SharedScopeQueries.ReadBoardTitleAsync(fixture.Api, boardId));
        Assert.Equal(1, await SharedScopeQueries.CountCardsAsync(fixture.Api, boardId));
        Assert.Equal(0, await SharedScopeQueries.CountOwnedScopesAsync(fixture.Api, other.AccountId, TestBoardType.ScopeKindName));
    }

    [Theory]
    [InlineData("member", "active", "applied", null)]
    [InlineData("readOnly", "active", "rejected", "noAccess")]
    [InlineData("member", "former", "rejected", "noAccess")]
    public async Task Sync_CommandFromMemberEntry_DecidesByRoleAndState(string role, string state, string expectedStatus, string? expectedReason)
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        await ApplyAsync(owner, owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        await new DatabaseAdministration(fixture.ConnectionString).InsertMemberEntryAsync(
            boardId,
            member.AccountId,
            now.AddMilliseconds(1),
            role,
            state
        );
        var create = member.CreateCard(boardId, Guid.NewGuid(), now, "bread");

        var response = await member.SyncAllAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), expectedStatus, expectedReason), Assert.Single(response.Results));
        Assert.Equal(expectedStatus == "applied" ? 1 : 0, await SharedScopeQueries.CountCardsAsync(fixture.Api, boardId));
    }

    [Theory]
    [InlineData("createCard")]
    [InlineData("setCardFields")]
    [InlineData("deleteCard")]
    [InlineData("setBoardTitle")]
    [InlineData("deleteBoard")]
    public async Task Sync_NonRootCommandIntoMissingScope_RejectedAndCreatesNothing(string commandName)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var command = commandName switch
        {
            "createCard" => device.CreateCard(boardId, cardId, now, "milk"),
            "setCardFields" => device.SetCardFields(boardId, cardId, now, new JsonObject { ["text"] = "milk" }),
            "deleteCard" => device.DeleteCard(boardId, cardId, now),
            "setBoardTitle" => device.SetBoardTitle(boardId, now, "board"),
            "deleteBoard" => device.DeleteBoard(boardId, now),
            _ => throw new ArgumentOutOfRangeException(nameof(commandName), commandName, null),
        };

        var response = await device.SyncAllAsync(command);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), "rejected", "noAccess"), Assert.Single(response.Results));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
        Assert.Null(await SharedScopeQueries.ReadCardAsync(fixture.Api, command["recordId"]!.GetValue<Guid>()));
    }

    [Theory]
    [InlineData("cardInOwnPersonalScope", "invalid")]
    [InlineData("noteInOwnBoard", "invalid")]
    [InlineData("noteInForeignBoard", "noAccess")]
    public async Task Sync_CommandOfOtherScopeKind_RejectedInvalidOnlyWithAccess(string scenario, string expectedReason)
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        await ApplyAsync(owner, owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        var sender = scenario == "noteInForeignBoard" ? await SyncTestDevice.SignInAsync(fixture.Api) : owner;
        var command = scenario switch
        {
            "cardInOwnPersonalScope" => sender.CreateCard(sender.PersonalScopeId, cardId, now, "milk"),
            "noteInOwnBoard" or "noteInForeignBoard" => sender.CreateNote(noteId, now, "note", scopeId: boardId),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        var response = await sender.SyncAllAsync(command);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), "rejected", expectedReason), Assert.Single(response.Results));
        Assert.Null(await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId));
        Assert.Null(await TestNoteQueries.ReadTitleAsync(fixture.Api, noteId));
        Assert.Equal(1, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))?.Version);
    }

    [Fact]
    public async Task Sync_RootDeleteByMember_RejectedNoAccess()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        await CreateBoardWithCardAsync(owner, boardId, Guid.NewGuid(), now);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        await JoinAsync(member, boardId, now);
        var delete = member.DeleteBoard(boardId, now);

        var response = await member.SyncAllAsync(delete);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(delete), "rejected", "noAccess"), Assert.Single(response.Results));
        Assert.NotNull(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
        Assert.Equal(1, await SharedScopeQueries.CountCardsAsync(fixture.Api, boardId));
        Assert.Equal(2, (await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId)).Count);
    }

    [Fact]
    public async Task Sync_CardDeleteByMember_RejectedNoAccessByMinimumRole()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await CreateBoardWithCardAsync(owner, boardId, cardId, now);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        await JoinAsync(member, boardId, now);
        var memberDelete = member.DeleteCard(boardId, cardId, now);
        var ownerDelete = owner.DeleteCard(boardId, cardId, now);

        var memberResult = Assert.Single((await member.SyncAllAsync(memberDelete)).Results);
        var cardCountAfterMemberDelete = await SharedScopeQueries.CountCardsAsync(fixture.Api, boardId);
        var ownerResult = Assert.Single((await owner.SyncAllAsync(ownerDelete)).Results);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(memberDelete), "rejected", "noAccess"), memberResult);
        Assert.Equal(1, cardCountAfterMemberDelete);
        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(ownerDelete), "applied"), ownerResult);
        Assert.Equal(0, await SharedScopeQueries.CountCardsAsync(fixture.Api, boardId));
    }

    [Theory]
    [InlineData("create", "changedBy")]
    [InlineData("create", "createdBy")]
    [InlineData("create", "doneBy")]
    [InlineData("create", "createdAt")]
    [InlineData("setFields", "changedBy")]
    public async Task Sync_SentPersonReference_RejectedInvalid(string kind, string field)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await ApplyAsync(device, device.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var value = field == "createdAt" ? JsonValue.Create(now.ToUnixTimeMilliseconds()) : JsonValue.Create(ownerMemberId);
        JsonObject command;
        if (kind == "setFields")
        {
            await ApplyAsync(device, device.CreateCard(boardId, cardId, now.AddSeconds(-20), "milk"));
            command = device.SetCardFields(boardId, cardId, now, new JsonObject { ["text"] = "changed", [field] = value });
        }
        else
        {
            command = SyncTestCommands.WithField(device.CreateCard(boardId, cardId, now, "milk"), field, value);
        }

        var response = await device.SyncAllAsync(command);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), "rejected", "invalid"), Assert.Single(response.Results));
        var card = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        Assert.Equal(kind == "setFields" ? ("milk", (Guid?)ownerMemberId) : (null, null), (card?.Text, card?.ChangedByMemberId));
    }

    [Fact]
    public async Task Sync_SetFieldsByMember_FillsChangedByAndDoneByWithActingEntry()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await ApplyAsync(owner, owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await ApplyAsync(owner, owner.CreateCard(boardId, cardId, now.AddSeconds(-10), "milk"));
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        var memberId = await JoinAsync(member, boardId, now);

        await ApplyAsync(member, member.SetCardFields(boardId, cardId, now.AddSeconds(-5), new JsonObject { ["done"] = true }));
        var afterDone = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        await ApplyAsync(owner, owner.SetCardFields(boardId, cardId, now.AddSeconds(-4), new JsonObject { ["text"] = "oat milk" }));
        var afterText = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        await ApplyAsync(member, member.SetCardFields(boardId, cardId, now.AddSeconds(-3), new JsonObject { ["done"] = false }));
        var afterUndone = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);

        var createdAt = now.AddSeconds(-10);
        Assert.Equal(
            new (Guid?, Guid?, Guid?, DateTimeOffset?)[]
            {
                (memberId, memberId, ownerMemberId, createdAt),
                (ownerMemberId, memberId, ownerMemberId, createdAt),
                (memberId, null, ownerMemberId, createdAt),
            },
            new[] { afterDone, afterText, afterUndone }.Select(card =>
                (card?.ChangedByMemberId, card?.DoneByMemberId, card?.CreatedByMemberId, card?.CreatedAt)
            )
        );
    }

    [Fact]
    public async Task Sync_CreateOnExistingId_KeepsCreatorAndCreatedAt()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await ApplyAsync(owner, owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await ApplyAsync(owner, owner.CreateCard(boardId, cardId, now.AddSeconds(-10), "milk"));
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        var memberId = await JoinAsync(member, boardId, now);
        var create = member.CreateCard(boardId, cardId, now.AddSeconds(-5), "other");

        var response = await member.SyncAllAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "applied"), Assert.Single(response.Results));
        var card = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        Assert.Equal(
            ("other", ownerMemberId, now.AddSeconds(-10), memberId),
            (card?.Text, card?.CreatedByMemberId, card?.CreatedAt, card?.ChangedByMemberId)
        );
    }

    [Fact]
    public async Task Sync_RootDeleteWithRecordsReferencingOwnerEntry_DeletesScope()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        Guid[] cardIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        await ApplyAsync(owner, owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await ApplyAsync(
            owner,
            owner.CreateCard(boardId, cardIds[0], now.AddSeconds(-20), "milk"),
            owner.CreateCard(boardId, cardIds[1], now.AddSeconds(-20), "bread"),
            owner.SetCardFields(boardId, cardIds[1], now.AddSeconds(-15), new JsonObject { ["done"] = true })
        );
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        await JoinAsync(member, boardId, now);
        await ApplyAsync(member, member.CreateCard(boardId, cardIds[2], now.AddSeconds(-10), "butter"));
        var delete = owner.DeleteBoard(boardId, now);

        var response = await owner.SyncAllAsync(delete);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(delete), "applied"), Assert.Single(response.Results));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
        Assert.Empty(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId));
        foreach (var cardId in cardIds)
        {
            Assert.Null(await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId));
        }

        Assert.Null(await SharedScopeQueries.ReadBoardTitleAsync(fixture.Api, boardId));
    }

    [Fact]
    public async Task Sync_ConcurrentRootCreatesOfSameIdByTwoAccounts_AppliesOneAndRejectsOtherNoAccess()
    {
        SyncTestDevice[] devices = [await SyncTestDevice.SignInAsync(fixture.Api), await SyncTestDevice.SignInAsync(fixture.Api)];
        string[] titles = ["a", "b"];
        var boardId = Guid.NewGuid();
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var administration = new DatabaseAdministration(fixture.ConnectionString);
        Task<SyncResponse>[] creates;
        await using (await administration.LockScopesTableAsync())
        {
            creates = [.. devices.Select((device, index) => device.SyncAllAsync(device.CreateBoard(boardId, now, titles[index])))];
            await administration.WaitForLockWaitersAsync(devices.Length);
        }

        var responses = await Task.WhenAll(creates);

        var results = responses.Select(response => Assert.Single(response.Results)).ToArray();
        var winner = Assert.Single(Enumerable.Range(0, devices.Length), index => results[index].Status == "applied");
        var loser = 1 - winner;
        Assert.Equal(("rejected", "noAccess"), (results[loser].Status, results[loser].Reason));
        Assert.Equal(devices[winner].AccountId, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))?.OwnerAccountId);
        var entry = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId));
        Assert.Equal(((Guid?)devices[winner].AccountId, "owner"), (entry.AccountId, entry.Role));
    }

    private static async Task ApplyAsync(SyncTestDevice device, params JsonObject[] commands)
    {
        var response = await device.SyncAllAsync(commands);
        Assert.Equal(commands.Select(command => new SyncCommandResult(SyncTestCommands.GetId(command), "applied")), response.Results);
    }

    private static async Task CreateBoardWithCardAsync(SyncTestDevice owner, Guid boardId, Guid cardId, DateTimeOffset now)
    {
        await ApplyAsync(owner, owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await ApplyAsync(owner, owner.CreateCard(boardId, cardId, now.AddSeconds(-20), "milk"));
    }

    private async Task<Guid> ReadOwnerMemberIdAsync(Guid boardId) =>
        Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId)).Id;

    private async Task<Guid> JoinAsync(SyncTestDevice member, Guid boardId, DateTimeOffset now)
    {
        var memberId = await new DatabaseAdministration(fixture.ConnectionString).InsertMemberEntryAsync(
            boardId,
            member.AccountId,
            now.AddMilliseconds(1)
        );
        await member.SyncAllAsync();
        return memberId;
    }
}
