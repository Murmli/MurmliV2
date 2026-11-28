using System.Text.Json.Nodes;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsScopeTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    [Fact]
    public async Task Sync_RootCreate_CreatesScopeWithOwnerEntry()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var boardId = Guid.NewGuid();
        var create = device.CreateBoard(boardId, fixture.Api.TimeProvider.GetUtcNow(), "board");

        var response = await device.SyncAllAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "applied"), Assert.Single(response.Results));
        var block = Assert.Single(response.Scopes);
        Assert.Equal((boardId, TestBoardType.ScopeKindName, true), (block.ScopeId, block.Kind, block.FullState));
        var record = Assert.Single(block.Records);
        Assert.Equal((TestBoardType.DataTypeName, boardId, """{"title":"board"}"""), (record.DataType, record.Id, record.Fields.GetRawText()));
        Assert.Empty(block.DeletedRecords);
        var member = Assert.Single(block.Members);
        Assert.Equal(new SyncMember(member.MemberId, "", "owner", "active", true), member);
        var scope = await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId);
        Assert.NotNull(scope);
        var tokenScopes = SyncTokenFormat.Decode(response.Token).Scopes;
        Assert.Equal(
            new[] { device.PersonalScopeId, boardId }.OrderBy(scopeId => scopeId.ToString(), StringComparer.Ordinal),
            tokenScopes.Select(tokenScope => tokenScope.ScopeId)
        );
        Assert.Equal((boardId, scope.Epoch, 1L), Assert.Single(tokenScopes, tokenScope => tokenScope.ScopeId == boardId));
        Assert.Equal(
            (TestBoardType.ScopeKindName, device.AccountId, 1L, 0L),
            (scope.Kind, scope.OwnerAccountId, scope.Version, scope.TombstoneFloor)
        );
        var entry = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId));
        Assert.Equal(
            (member.MemberId, (Guid?)device.AccountId, "", "owner", SyncMemberState.Active),
            (entry.Id, entry.AccountId, entry.Name, entry.Role, entry.State)
        );
    }

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
        Assert.Equal(new SyncRemovedScope(boardId, false), Assert.Single(response.RemovedScopes));
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
        Assert.DoesNotContain(response.Scopes, block => block.ScopeId == boardId);
        Assert.Equal(new SyncRemovedScope(boardId, false), Assert.Single(response.RemovedScopes));
        Assert.DoesNotContain("milk", other.ResponseBodies[^1], StringComparison.Ordinal);
        Assert.Equal(versionBefore, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))?.Version);
        Assert.Equal("milk", (await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId))?.Text);
        Assert.Equal("board", await SharedScopeQueries.ReadBoardTitleAsync(fixture.Api, boardId));
        Assert.Equal(1, await SharedScopeQueries.CountCardsAsync(fixture.Api, boardId));
        Assert.Equal(0, await SharedScopeQueries.CountOwnedScopesAsync(fixture.Api, other.AccountId, TestBoardType.ScopeKindName));
        Assert.Empty((await owner.SyncAllAsync()).Scopes);
    }

    [Theory]
    [InlineData("member", "active", "applied", null, true)]
    [InlineData("readOnly", "active", "rejected", "noAccess", true)]
    [InlineData("member", "former", "rejected", "noAccess", false)]
    public async Task Sync_CommandFromMemberEntry_DecidesByRoleAndState(
        string role,
        string state,
        string expectedStatus,
        string? expectedReason,
        bool visibleToMember
    )
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        var memberId = await new DatabaseAdministration(fixture.ConnectionString).InsertMemberEntryAsync(
            boardId,
            member.AccountId,
            now.AddMilliseconds(1),
            role,
            state
        );
        var create = member.CreateCard(boardId, cardId, now, "bread");

        var response = await member.SyncAllAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), expectedStatus, expectedReason), Assert.Single(response.Results));
        if (visibleToMember)
        {
            var block = Assert.Single(response.Scopes, block => block.ScopeId == boardId);
            Assert.True(block.FullState);
            Assert.Equal(
                new[] { new SyncMember(ownerMemberId, "", "owner", "active", false), new SyncMember(memberId, "", role, state, true) },
                block.Members
            );
            Assert.Equal(
                expectedStatus == "applied"
                    ? new[] { (TestBoardType.DataTypeName, boardId), (TestCardType.DataTypeName, cardId) }
                    : new[] { (TestBoardType.DataTypeName, boardId) },
                block.Records.Select(record => (record.DataType, record.Id))
            );
            Assert.Empty(block.DeletedRecords);
            Assert.Empty(response.RemovedScopes);
        }
        else
        {
            Assert.DoesNotContain(response.Scopes, block => block.ScopeId == boardId);
            Assert.Equal(new SyncRemovedScope(boardId, false), Assert.Single(response.RemovedScopes));
        }

        Assert.Equal(expectedStatus == "applied" ? 1 : 0, await SharedScopeQueries.CountCardsAsync(fixture.Api, boardId));
        Assert.Equal(
            new[] { new SyncMember(ownerMemberId, "", "owner", "active", true), new SyncMember(memberId, "", role, state, false) },
            Assert.Single((await owner.SyncAllAsync()).Scopes, block => block.ScopeId == boardId).Members
        );
    }

    [Fact]
    public async Task Sync_ForeignScope_NeverReturned()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var boardId = Guid.NewGuid();
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, fixture.Api.TimeProvider.GetUtcNow(), "board"));
        var ownerBoardEntry = Assert.Single(SyncTokenFormat.Decode(owner.Token!).Scopes, tokenScope => tokenScope.ScopeId == boardId);
        var other = await SyncTestDevice.SignInAsync(fixture.Api, firstSync: false);

        var withoutToken = await other.SyncAllAsync();
        var otherToken = SyncTokenFormat.Decode(withoutToken.Token);
        var personalEntry = Assert.Single(otherToken.Scopes);
        other.Token = SyncTokenFormat.Encode(
            otherToken.DatabaseIdentity,
            [.. new[] { personalEntry, ownerBoardEntry }.OrderBy(tokenScope => tokenScope.ScopeId.ToString(), StringComparer.Ordinal)]
        );
        var withForgedToken = await other.SyncAllAsync();

        Assert.Equal(other.PersonalScopeId, Assert.Single(withoutToken.Scopes).ScopeId);
        Assert.True(withoutToken.CoversAllScopes);
        Assert.Equal(other.PersonalScopeId, personalEntry.ScopeId);
        Assert.DoesNotContain(withForgedToken.Scopes, block => block.ScopeId == boardId);
        Assert.Equal(new SyncRemovedScope(boardId, false), Assert.Single(withForgedToken.RemovedScopes));
        Assert.Equal(other.PersonalScopeId, Assert.Single(SyncTokenFormat.Decode(withForgedToken.Token).Scopes).ScopeId);
    }

    [Fact]
    public async Task Sync_ScopeOfUnlistedKind_NeitherSentNorReportedRemoved()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var boardId = Guid.NewGuid();
        await device.ApplyAllAsync(device.CreateBoard(boardId, fixture.Api.TimeProvider.GetUtcNow(), "board"));

        var notesOnly = await device.SyncAsync();
        var notesOnlyUnchanged = await device.SyncAsync();
        var allDataTypes = await device.SyncAllAsync();

        Assert.All(
            new[] { notesOnly, notesOnlyUnchanged },
            response =>
            {
                Assert.Empty(response.Scopes);
                Assert.Empty(response.RemovedScopes);
                Assert.Equal(device.PersonalScopeId, Assert.Single(SyncTokenFormat.Decode(response.Token).Scopes).ScopeId);
            }
        );
        var block = Assert.Single(allDataTypes.Scopes);
        Assert.Equal((boardId, true), (block.ScopeId, block.FullState));
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
        Assert.Equal(new SyncRemovedScope(boardId, false), Assert.Single(response.RemovedScopes));
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
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
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
    public async Task Sync_SeveralScopes_OrdersPersonalScopeFirstThenByScopeId()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardSortingBeforePersonalScope = new Guid($"00000000{Guid.NewGuid().ToString()[8..]}");
        Guid[] boardIds = [boardSortingBeforePersonalScope, Guid.NewGuid(), Guid.NewGuid()];
        await device.ApplyAllAsync([.. boardIds.Select(boardId => device.CreateBoard(boardId, now, "board"))]);

        var response = await device.OtherDevice().SyncAllAsync();

        Assert.Equal(
            new[] { device.PersonalScopeId }.Concat(boardIds.OrderBy(boardId => boardId.ToString(), StringComparer.Ordinal)),
            response.Scopes.Select(block => block.ScopeId)
        );
        Assert.Equal(
            boardIds.Append(device.PersonalScopeId).OrderBy(scopeId => scopeId.ToString(), StringComparer.Ordinal),
            SyncTokenFormat.Decode(response.Token).Scopes.Select(tokenScope => tokenScope.ScopeId)
        );
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
    [InlineData("memberDelete")]
    [InlineData("memberDeleteResent")]
    [InlineData("ownerDeleteAheadOfClock")]
    public async Task Sync_RootDeleteNotApplied_IncludesFullScopeState(string scenario)
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await CreateBoardWithCardAsync(owner, boardId, cardId, now);
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        var memberId = await JoinAsync(member, boardId, now);
        var isOwnerSender = scenario == "ownerDeleteAheadOfClock";
        var sender = isOwnerSender ? owner : member;
        var delete = isOwnerSender ? owner.DeleteBoard(boardId, now.AddMinutes(6)) : member.DeleteBoard(boardId, now);
        var expectedResult = scenario switch
        {
            "memberDelete" => new SyncCommandResult(SyncTestCommands.GetId(delete), "rejected", "noAccess"),
            "memberDeleteResent" => new SyncCommandResult(
                SyncTestCommands.GetId(delete),
                "alreadyProcessed",
                Original: new SyncOriginalResult("rejected", "noAccess")
            ),
            "ownerDeleteAheadOfClock" => new SyncCommandResult(SyncTestCommands.GetId(delete), "rejected", "clockAhead"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
        if (scenario == "memberDeleteResent")
        {
            await member.SyncAllAsync(delete);
        }

        var response = await sender.SyncAllAsync(delete);

        Assert.Equal(expectedResult, Assert.Single(response.Results));
        var block = Assert.Single(response.Scopes, block => block.ScopeId == boardId);
        Assert.True(block.FullState);
        Assert.Equal(
            new[] { (TestBoardType.DataTypeName, boardId), (TestCardType.DataTypeName, cardId) },
            block.Records.Select(record => (record.DataType, record.Id))
        );
        Assert.Empty(block.DeletedRecords);
        Assert.Equal(
            new[]
            {
                new SyncMember(ownerMemberId, "", "owner", "active", isOwnerSender),
                new SyncMember(memberId, "", "member", "active", !isOwnerSender),
            },
            block.Members
        );
        Assert.NotNull(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
    }

    [Fact]
    public async Task Sync_ScopeDeletedOnOneDevice_ReportedRemovedByOwnAccountOnOther()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await CreateBoardWithCardAsync(device, boardId, cardId, now);
        var otherDevice = device.OtherDevice();
        await otherDevice.SyncAllAsync();
        var delete = device.DeleteBoard(boardId, now);

        var response = await device.SyncAllAsync(delete);
        var otherDeviceResponse = await otherDevice.SyncAllAsync();

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(delete), "applied"), Assert.Single(response.Results));
        Assert.DoesNotContain(response.Scopes, block => block.ScopeId == boardId);
        Assert.Equal(new SyncRemovedScope(boardId, true), Assert.Single(response.RemovedScopes));
        Assert.DoesNotContain(otherDeviceResponse.Scopes, block => block.ScopeId == boardId);
        Assert.Equal(new SyncRemovedScope(boardId, true), Assert.Single(otherDeviceResponse.RemovedScopes));
        Assert.DoesNotContain(SyncTokenFormat.Decode(otherDeviceResponse.Token).Scopes, tokenScope => tokenScope.ScopeId == boardId);
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
        Assert.Empty(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId));
        Assert.Null(await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId));
        var processed = Assert.Single(
            await TestNoteQueries.ReadProcessedCommandsAsync(fixture.Api, device.AccountId),
            command => command.CommandId == SyncTestCommands.GetId(delete)
        );
        Assert.Equal((SyncResultStatus.Applied, boardId, boardId), (processed.Status, processed.RecordId, processed.ScopeId));
    }

    [Fact]
    public async Task Sync_ScopeDeletedByOwner_ReportedRemovedNotByOwnAccountToMember()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        await JoinAsync(member, boardId, now);
        await member.ApplyAllAsync(member.SetBoardTitle(boardId, now.AddSeconds(-10), "renamed"));
        var memberDelete = member.DeleteBoard(boardId, now);
        var memberDeleteResponse = await member.SyncAllAsync(memberDelete);
        await owner.ApplyAllAsync(owner.DeleteBoard(boardId, now));

        var response = await member.SyncAllAsync();

        Assert.Equal(
            new SyncCommandResult(SyncTestCommands.GetId(memberDelete), "rejected", "noAccess"),
            Assert.Single(memberDeleteResponse.Results)
        );
        Assert.Equal(new SyncRemovedScope(boardId, false), Assert.Single(response.RemovedScopes));
        Assert.DoesNotContain(response.Scopes, block => block.ScopeId == boardId);
    }

    [Fact]
    public async Task Sync_ScopeDeletedBeforeOtherDeviceKnewIt_ReportedRemoved()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var tokenBeforeBoard = device.Token;
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        await device.ApplyAllAsync(device.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await device.ApplyAllAsync(device.DeleteBoard(boardId, now));
        var otherDevice = device.OtherDevice();
        otherDevice.Token = tokenBeforeBoard;
        var create = otherDevice.CreateCard(boardId, Guid.NewGuid(), now, "late");

        var response = await otherDevice.SyncAllAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "rejected", "noAccess"), Assert.Single(response.Results));
        Assert.Equal(new SyncRemovedScope(boardId, true), Assert.Single(response.RemovedScopes));
        Assert.DoesNotContain(response.Scopes, block => block.ScopeId == boardId);
    }

    [Fact]
    public async Task Sync_FailedRootCreateOfNewScope_NotReportedRemoved()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var boardId = Guid.NewGuid();
        var create = device.CreateBoard(boardId, fixture.Api.TimeProvider.GetUtcNow(), TestBoardType.FailingTitle);

        var response = await device.SyncAllAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "failed"), Assert.Single(response.Results));
        Assert.Empty(response.RemovedScopes);
        Assert.DoesNotContain(response.Scopes, block => block.ScopeId == boardId);
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
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
        await device.ApplyAllAsync(device.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var value = field == "createdAt" ? JsonValue.Create(now.ToUnixTimeMilliseconds()) : JsonValue.Create(ownerMemberId);
        JsonObject command;
        if (kind == "setFields")
        {
            await device.ApplyAllAsync(device.CreateCard(boardId, cardId, now.AddSeconds(-20), "milk"));
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
    public async Task Sync_Create_FillsChangedByWithOwnEntry()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await device.ApplyAllAsync(device.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var createdAt = now.AddSeconds(-10);
        var create = device.CreateCard(boardId, cardId, createdAt, "milk");

        var response = await device.SyncAllAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "applied"), Assert.Single(response.Results));
        var record = Assert.Single(Assert.Single(response.Scopes, block => block.ScopeId == boardId).Records);
        Assert.Equal(
            (
                TestCardType.DataTypeName,
                cardId,
                $$"""{"text":"milk","count":null,"measure":null,"done":false,"changedBy":"{{ownerMemberId}}","doneBy":null,"createdAt":{{createdAt.ToUnixTimeMilliseconds()}}}"""
            ),
            (record.DataType, record.Id, record.Fields.GetRawText())
        );
        var card = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        Assert.Equal((ownerMemberId, ownerMemberId, createdAt), (card?.ChangedByMemberId, card?.CreatedByMemberId, card?.CreatedAt));
    }

    [Fact]
    public async Task Sync_SetFieldsByMember_FillsChangedByAndDoneByWithActingEntry()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await owner.ApplyAllAsync(owner.CreateCard(boardId, cardId, now.AddSeconds(-10), "milk"));
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        var memberId = await JoinAsync(member, boardId, now);

        await member.ApplyAllAsync(member.SetCardFields(boardId, cardId, now.AddSeconds(-5), new JsonObject { ["done"] = true }));
        var afterDone = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        await owner.ApplyAllAsync(owner.SetCardFields(boardId, cardId, now.AddSeconds(-4), new JsonObject { ["text"] = "oat milk" }));
        var afterText = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        await member.ApplyAllAsync(member.SetCardFields(boardId, cardId, now.AddSeconds(-3), new JsonObject { ["done"] = false }));
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
    public async Task Sync_Create_StoresCreatorWithoutSendingIt()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await CreateBoardWithCardAsync(owner, boardId, cardId, now);
        var ownerMemberId = await ReadOwnerMemberIdAsync(boardId);
        var creatorBeforeChange = (await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId))?.CreatedByMemberId;
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        var memberId = await JoinAsync(member, boardId, now);
        await member.ApplyAllAsync(member.SetCardFields(boardId, cardId, now.AddSeconds(-10), new JsonObject { ["text"] = "changed" }));
        var otherDevice = owner.OtherDevice();

        var otherDeviceResponse = await otherDevice.SyncAllAsync();

        var card = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        Assert.Equal((ownerMemberId, ownerMemberId, memberId), (creatorBeforeChange, card?.CreatedByMemberId, card?.ChangedByMemberId));
        Assert.Contains(Assert.Single(otherDeviceResponse.Scopes, block => block.ScopeId == boardId).Records, record => record.Id == cardId);
        Assert.All(
            owner.ResponseBodies.Concat(member.ResponseBodies).Concat(otherDevice.ResponseBodies),
            body => Assert.DoesNotContain("\"createdBy\"", body, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Sync_CreateOnExistingId_KeepsCreatorAndCreatedAt()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await owner.ApplyAllAsync(owner.CreateCard(boardId, cardId, now.AddSeconds(-10), "milk"));
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
        var record = Assert.Single(Assert.Single(response.Scopes, block => block.ScopeId == boardId).Records, record => record.Id == cardId);
        Assert.Equal(now.AddSeconds(-10).ToUnixTimeMilliseconds(), record.Fields.GetProperty("createdAt").GetInt64());
    }

    [Fact]
    public async Task Sync_RootDeleteWithRecordsReferencingOwnerEntry_DeletesScope()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        Guid[] cardIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await owner.ApplyAllAsync(
            owner.CreateCard(boardId, cardIds[0], now.AddSeconds(-20), "milk"),
            owner.CreateCard(boardId, cardIds[1], now.AddSeconds(-20), "bread"),
            owner.SetCardFields(boardId, cardIds[1], now.AddSeconds(-15), new JsonObject { ["done"] = true })
        );
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        await JoinAsync(member, boardId, now);
        await member.ApplyAllAsync(member.CreateCard(boardId, cardIds[2], now.AddSeconds(-10), "butter"));
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
    public async Task Sync_AnyResponse_ContainsNoForeignAccountId()
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await CreateBoardWithCardAsync(owner, boardId, cardId, now);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        await JoinAsync(member, boardId, now);
        await member.ApplyAllAsync(member.SetCardFields(boardId, cardId, now.AddSeconds(-10), new JsonObject { ["text"] = "changed" }));
        await owner.SyncAllAsync();
        await owner.ApplyAllAsync(owner.DeleteCard(boardId, cardId, now));
        await member.SyncAllAsync();
        var otherMemberDevice = member.OtherDevice();
        await otherMemberDevice.SyncAllAsync();
        await owner.ApplyAllAsync(owner.DeleteBoard(boardId, now));

        await member.SyncAllAsync();

        Assert.All(
            owner.ResponseBodies.Concat(member.ResponseBodies).Concat(otherMemberDevice.ResponseBodies),
            body =>
            {
                foreach (var accountId in new[] { owner.AccountId, member.AccountId })
                {
                    Assert.DoesNotContain(accountId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(accountId.ToString("N"), body, StringComparison.OrdinalIgnoreCase);
                }
            }
        );
    }

    [Theory]
    [InlineData("liveRecord")]
    [InlineData("tombstonedRecord")]
    public async Task Sync_ForeignRecordId_RejectedLikeNoAccessWithoutContent(string scenario)
    {
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var foreignBoardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await owner.ApplyAllAsync(owner.CreateBoard(foreignBoardId, now.AddSeconds(-30), "board"));
        await owner.ApplyAllAsync(owner.CreateCard(foreignBoardId, cardId, now.AddSeconds(-20), "secret"));
        var ownerMemberId = await ReadOwnerMemberIdAsync(foreignBoardId);
        var isTombstoned = scenario == "tombstonedRecord";
        if (isTombstoned)
        {
            await owner.ApplyAllAsync(owner.DeleteCard(foreignBoardId, cardId, now.AddSeconds(-10)));
        }

        var other = await SyncTestDevice.SignInAsync(fixture.Api);
        var ownBoardId = Guid.NewGuid();
        await other.ApplyAllAsync(other.CreateBoard(ownBoardId, now.AddSeconds(-30), "board"));
        var command = scenario switch
        {
            "liveRecord" => other.SetCardFields(ownBoardId, cardId, now, new JsonObject { ["text"] = "x" }),
            "tombstonedRecord" => other.CreateCard(ownBoardId, cardId, now, "x"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        var response = await other.SyncAllAsync(command);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), "rejected", "noAccess"), Assert.Single(response.Results));
        var block = Assert.Single(response.Scopes, block => block.ScopeId == ownBoardId);
        Assert.DoesNotContain(block.Records, record => record.Id == cardId);
        Assert.Equal(new[] { new SyncDeletedRecord(TestCardType.DataTypeName, cardId) }, block.DeletedRecords);
        Assert.All(
            new[] { foreignBoardId.ToString(), "secret", ownerMemberId.ToString() },
            content => Assert.DoesNotContain(content, other.ResponseBodies[^1], StringComparison.OrdinalIgnoreCase)
        );
        var card = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        Assert.Equal(isTombstoned ? (null, null) : ("secret", (Guid?)foreignBoardId), (card?.Text, card?.ScopeId));
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
        Assert.DoesNotContain(responses[loser].Scopes, block => block.ScopeId == boardId);
        Assert.Equal(new SyncRemovedScope(boardId, false), Assert.Single(responses[loser].RemovedScopes));
        Assert.Equal(devices[winner].AccountId, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))?.OwnerAccountId);
        var entry = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId));
        Assert.Equal(((Guid?)devices[winner].AccountId, "owner"), (entry.AccountId, entry.Role));
    }

    private static async Task CreateBoardWithCardAsync(SyncTestDevice owner, Guid boardId, Guid cardId, DateTimeOffset now)
    {
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        await owner.ApplyAllAsync(owner.CreateCard(boardId, cardId, now.AddSeconds(-20), "milk"));
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
