using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Protocol;
using Murmli.Backend.Sync.Writing;
using OpenTelemetry.Logs;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsChangeTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    private const string OriginalTitle = "original";

    private const string OriginalFields = """{"title":"original","color":null,"weight":null,"pinned":false}""";

    private static readonly TimeSpan PastRetention = TimeSpan.FromDays(45) + TimeSpan.FromMinutes(1);

    private static readonly SyncServerWriteResult AppliedResult = new("applied", null, null);

    [Theory]
    [InlineData("lostToNewer")]
    [InlineData("targetDeleted")]
    [InlineData("targetDeletedByOwnDevice")]
    [InlineData("invalidClaimed")]
    [InlineData("rejectedByRule")]
    [InlineData("clockAhead")]
    [InlineData("tooOld")]
    [InlineData("alreadyProcessed")]
    [InlineData("failed")]
    [InlineData("timeMissing")]
    [InlineData("timeAsString")]
    [InlineData("kindMissing")]
    [InlineData("unknownKind")]
    [InlineData("idMalformed")]
    [InlineData("idVersion7")]
    [InlineData("recordIdVersion7")]
    public async Task Sync_NotAppliedCommand_IncludesCurrentTargetState(string scenario)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        var create = device.CreateNote(noteId, now.AddSeconds(-10), OriginalTitle);
        await device.SyncAsync(create);
        var change = device.SetNoteFields(noteId, now, SyncTestCommands.TitleFields("changed"));
        var changeId = SyncTestCommands.GetId(change);
        var missingNoteId = Guid.NewGuid();
        var version7RecordId = Guid.CreateVersion7();
        (JsonObject Command, SyncCommandResult Result, Guid? DeletedId, bool IsClaimed) expected = scenario switch
        {
            "lostToNewer" => (
                SyncTestCommands.With(change, "time", now.AddSeconds(-20).ToUnixTimeMilliseconds()),
                new(changeId, "ignored", Cause: "lostToNewer"),
                null,
                true
            ),
            "targetDeleted" => (
                SyncTestCommands.With(change, "recordId", missingNoteId),
                new(changeId, "ignored", Cause: "targetDeleted"),
                missingNoteId,
                true
            ),
            "targetDeletedByOwnDevice" => (await DeleteNoteBeforeAsync(change), new(changeId, "ignored", Cause: "targetDeleted"), noteId, true),
            "invalidClaimed" => (SyncTestCommands.WithField(change, "subtitle", "unknown"), new(changeId, "rejected", "invalid"), null, true),
            "rejectedByRule" => (
                SyncTestCommands.WithField(change, "title", TestNoteType.RejectingTitle),
                new(changeId, "rejected", "invalid"),
                null,
                true
            ),
            "clockAhead" => (
                SyncTestCommands.With(change, "time", now.AddMinutes(6).ToUnixTimeMilliseconds()),
                new(changeId, "rejected", "clockAhead"),
                null,
                true
            ),
            "tooOld" => (
                SyncTestCommands.With(change, "time", now.AddDays(-46).ToUnixTimeMilliseconds()),
                new(changeId, "rejected", "tooOld"),
                null,
                true
            ),
            "alreadyProcessed" => (create, new(SyncTestCommands.GetId(create), "alreadyProcessed", Original: new("applied")), null, false),
            "failed" => (SyncTestCommands.WithField(change, "title", TestNoteType.FailingTitle), new(changeId, "failed"), null, false),
            "timeMissing" => (SyncTestCommands.Without(change, "time"), new(changeId, "rejected", "invalid"), null, false),
            "timeAsString" => (SyncTestCommands.With(change, "time", "1767225600000"), new(changeId, "rejected", "invalid"), null, false),
            "kindMissing" => (SyncTestCommands.Without(change, "kind"), new(changeId, "rejected", "invalid"), null, false),
            "unknownKind" => (SyncTestCommands.With(change, "kind", "move"), new(changeId, "rejected", "invalid"), null, false),
            "idMalformed" => (SyncTestCommands.With(change, "id", "not-a-guid"), new(null, "rejected", "invalid"), null, false),
            "idVersion7" => (SyncTestCommands.With(change, "id", Guid.CreateVersion7()), new(null, "rejected", "invalid"), null, false),
            "recordIdVersion7" => (
                SyncTestCommands.With(change, "recordId", version7RecordId),
                new(changeId, "rejected", "invalid"),
                version7RecordId,
                false
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };
        var processedCommandCount = (await TestNoteQueries.ReadProcessedCommandsAsync(fixture.Api, device.AccountId)).Count;

        var response = await device.SyncAsync(expected.Command);

        Assert.Equal(expected.Result, Assert.Single(response.Results));
        var block = Assert.Single(response.Scopes);
        Assert.Equal(device.PersonalScopeId, block.ScopeId);
        Assert.False(block.FullState);
        Guid[] expectedRecordIds = expected.DeletedId is null ? [noteId] : [];
        Assert.Equal(expectedRecordIds, block.Records.Select(record => record.Id));
        Assert.All(block.Records, record => Assert.Equal(OriginalFields, record.Fields.GetRawText()));
        SyncDeletedRecord[] expectedDeletions = expected.DeletedId is { } deletedId ? [new(TestNoteType.DataTypeName, deletedId)] : [];
        Assert.Equal(expectedDeletions, block.DeletedRecords);
        Assert.Equal(
            processedCommandCount + (expected.IsClaimed ? 1 : 0),
            (await TestNoteQueries.ReadProcessedCommandsAsync(fixture.Api, device.AccountId)).Count
        );

        async Task<JsonObject> DeleteNoteBeforeAsync(JsonObject command)
        {
            Assert.Equal("applied", Assert.Single((await device.SyncAsync(device.DeleteNote(noteId, now))).Results).Status);
            return command;
        }
    }

    [Theory]
    [InlineData(true, "lostToNewer")]
    [InlineData(false, "targetDeleted")]
    public async Task Sync_NotAppliedCommandWithoutToken_AddsNothingToFullState(bool targetExists, string expectedCause)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        await device.SyncAsync(device.CreateNote(noteId, now.AddSeconds(-10), OriginalTitle));
        var change = device.SetNoteFields(targetExists ? noteId : Guid.NewGuid(), now.AddSeconds(-20), SyncTestCommands.TitleFields("changed"));
        device.Token = null;

        var response = await device.SyncAsync(change);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(change), "ignored", Cause: expectedCause), Assert.Single(response.Results));
        var block = Assert.Single(response.Scopes);
        Assert.Equal(device.PersonalScopeId, block.ScopeId);
        Assert.True(block.FullState);
        var record = Assert.Single(block.Records);
        Assert.Equal((noteId, OriginalFields), (record.Id, record.Fields.GetRawText()));
        Assert.Empty(block.DeletedRecords);
    }

    [Theory]
    [InlineData("otherAccountsScope")]
    [InlineData("unknownScope")]
    public async Task Sync_CommandIntoInvisibleScope_ReportsScopeRemovedWithoutRecord(string target)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var owner = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var ownerNoteId = Guid.NewGuid();
        await owner.SyncAsync(owner.CreateNote(ownerNoteId, now.AddSeconds(-10), OriginalTitle));
        var command = target switch
        {
            "otherAccountsScope" => device.SetNoteFields(ownerNoteId, now, SyncTestCommands.TitleFields("changed"), owner.PersonalScopeId),
            "unknownScope" => device.CreateNote(Guid.NewGuid(), now, "note", scopeId: Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        var response = await device.SyncAsync(command);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), "rejected", "noAccess"), Assert.Single(response.Results));
        Assert.Equal(new SyncRemovedScope(command["scopeId"]!.GetValue<Guid>(), false), Assert.Single(response.RemovedScopes));
        Assert.Empty(response.Scopes);
        Assert.Equal(OriginalTitle, await TestNoteQueries.ReadTitleAsync(fixture.Api, ownerNoteId));
    }

    [Theory]
    [InlineData("noToken", true, true)]
    [InlineData("unreadable", true, true)]
    [InlineData("unknownFormat", true, true)]
    [InlineData("trailingByte", true, true)]
    [InlineData("descendingScopes", true, true)]
    [InlineData("foreignIdentity", true, true)]
    [InlineData("rotatedIdentity", true, true)]
    [InlineData("unknownScope", true, false)]
    [InlineData("otherEpoch", true, false)]
    [InlineData("belowTombstoneFloor", true, false)]
    [InlineData("aboveServerVersion", true, false)]
    [InlineData("changedByOtherDevice", false, false)]
    [InlineData("tombstoneFloorAtTokenVersion", false, false)]
    [InlineData("unchanged", null, false)]
    public async Task Sync_FullStateCondition_ReturnsFullStateExactlyThen(
        string condition,
        bool? expectedFullState,
        bool expectedCoversAllScopes
    )
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var versionZeroToken = device.Token;
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        var otherNoteId = Guid.NewGuid();
        await device.SyncAsync(device.CreateNote(noteId, now.AddSeconds(-10), "a"));
        var currentToken = device.Token!;
        var (databaseIdentity, tokenScopes) = SyncTokenFormat.Decode(currentToken);
        var personalScope = Assert.Single(tokenScopes);
        var unknownScopeId = Guid.NewGuid();
        var administration = new DatabaseAdministration(fixture.ConnectionString);
        switch (condition)
        {
            case "rotatedIdentity":
                await administration.RotateDatabaseIdentityAsync();
                break;
            case "belowTombstoneFloor":
                await administration.RaiseTombstoneFloorAsync(personalScope.ScopeId, 1);
                break;
            case "changedByOtherDevice":
                await device.ApplyOnOtherDeviceAsync(device.CreateNote(otherNoteId, now, "b"), device.DeleteNote(noteId, now));
                break;
            case "tombstoneFloorAtTokenVersion":
                await device.ApplyOnOtherDeviceAsync(device.CreateNote(otherNoteId, now, "b"));
                await administration.RaiseTombstoneFloorAsync(personalScope.ScopeId, 1);
                break;
        }

        (string? PresentedToken, Guid[] RecordIds, Guid[] DeletedIds, Guid[] RemovedScopeIds) expected = condition switch
        {
            "noToken" => (null, [noteId], [], []),
            "unreadable" => ("%%%", [noteId], [], []),
            "unknownFormat" => (SyncTokenFormat.Encode(databaseIdentity, tokenScopes, format: 2), [noteId], [], []),
            "trailingByte" => (Base64Url.EncodeToString([.. Base64Url.DecodeFromChars(currentToken), 0]), [noteId], [], []),
            "descendingScopes" => (
                SyncTokenFormat.Encode(
                    databaseIdentity,
                    [.. tokenScopes.Append((unknownScopeId, 0, 0)).OrderByDescending(scope => scope.ScopeId.ToString(), StringComparer.Ordinal)]
                ),
                [noteId],
                [],
                []
            ),
            "foreignIdentity" => (SyncTokenFormat.Encode(Guid.NewGuid(), tokenScopes), [noteId], [], []),
            "rotatedIdentity" => (currentToken, [noteId], [], []),
            "unknownScope" => (SyncTokenFormat.Encode(databaseIdentity, [(unknownScopeId, 0, 0)]), [noteId], [], [unknownScopeId]),
            "otherEpoch" => (
                SyncTokenFormat.Encode(databaseIdentity, [personalScope with { Epoch = personalScope.Epoch + 1 }]),
                [noteId],
                [],
                []
            ),
            "belowTombstoneFloor" => (versionZeroToken, [noteId], [], []),
            "aboveServerVersion" => (SyncTokenFormat.Encode(databaseIdentity, [personalScope with { Version = 99 }]), [noteId], [], []),
            "changedByOtherDevice" => (currentToken, [otherNoteId], [noteId], []),
            "tombstoneFloorAtTokenVersion" => (currentToken, [otherNoteId], [], []),
            "unchanged" => (currentToken, [], [], []),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null),
        };
        device.Token = expected.PresentedToken;

        var response = await device.SyncAsync();

        Assert.Empty(response.Results);
        Assert.Equal(expectedCoversAllScopes, response.CoversAllScopes);
        Assert.Equal(expectedFullState, response.Scopes.SingleOrDefault()?.FullState);
        Assert.All(response.Scopes, block => Assert.Equal(device.PersonalScopeId, block.ScopeId));
        Assert.Equal(expected.RecordIds, response.Scopes.SelectMany(block => block.Records).Select(record => record.Id));
        Assert.Equal(expected.DeletedIds, response.Scopes.SelectMany(block => block.DeletedRecords).Select(deletion => deletion.Id));
        Assert.Equal(expected.RemovedScopeIds.Select(scopeId => new SyncRemovedScope(scopeId, false)), response.RemovedScopes);
        Assert.Equal(await ReadCurrentTokenAsync(device.PersonalScopeId), response.Token);
    }

    [Fact]
    public async Task Sync_TokenBelowFloor_GetsFullStateWithoutDeletedRecords()
    {
        await using var api = await ApiHost.StartAsync(fixture.CreateDatabaseConnectionString(), testDataTypes: true);
        var writer = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var keptNoteId = Guid.NewGuid();
        var deletedNoteId = Guid.NewGuid();
        await writer.SyncAsync(writer.CreateNote(keptNoteId, now, "kept"), writer.CreateNote(deletedNoteId, now, "deleted"));
        var reader = writer.OtherDevice();
        await reader.SyncAsync();
        await writer.SyncAsync(writer.DeleteNote(deletedNoteId, now));
        api.TimeProvider.Advance(PastRetention);
        await api.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, (await SharedScopeQueries.ReadScopeAsync(api, writer.PersonalScopeId))!.TombstoneFloor);

        var response = await reader.SyncAsync();

        Assert.False(response.CoversAllScopes);
        var block = Assert.Single(response.Scopes);
        Assert.True(block.FullState);
        Assert.Equal([keptNoteId], block.Records.Select(record => record.Id));
        Assert.Empty(block.DeletedRecords);
    }

    [Fact]
    public async Task Sync_TokenAboveFloor_GetsDeltaWithDeletions()
    {
        await using var api = await ApiHost.StartAsync(fixture.CreateDatabaseConnectionString(), testDataTypes: true);
        var writer = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var expiredNoteId = Guid.NewGuid();
        var laterDeletedNoteId = Guid.NewGuid();
        await writer.SyncAsync(
            writer.CreateNote(expiredNoteId, now, "expired"),
            writer.CreateNote(laterDeletedNoteId, now, "later deleted"),
            writer.DeleteNote(expiredNoteId, now),
            writer.CreateNote(Guid.NewGuid(), now, "kept")
        );
        var reader = writer.OtherDevice();
        await reader.SyncAsync();
        api.TimeProvider.Advance(PastRetention);
        await writer.SyncAsync(writer.DeleteNote(laterDeletedNoteId, api.TimeProvider.GetUtcNow()));
        await api.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, (await SharedScopeQueries.ReadScopeAsync(api, writer.PersonalScopeId))!.TombstoneFloor);

        var response = await reader.SyncAsync();

        var block = Assert.Single(response.Scopes);
        Assert.False(block.FullState);
        Assert.Empty(block.Records);
        Assert.Equal([new SyncDeletedRecord(TestNoteType.DataTypeName, laterDeletedNoteId)], block.DeletedRecords);
    }

    [Fact]
    public async Task Sync_WithoutToken_CoversAllScopes()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var olderToken = device.Token;
        var now = fixture.Api.TimeProvider.GetUtcNow();
        Guid[] noteIds = [Guid.NewGuid(), Guid.NewGuid()];
        await device.SyncAsync(device.CreateNote(noteIds[0], now, "first"), device.CreateNote(noteIds[1], now, "second"));
        var otherDevice = device.OtherDevice();

        var withoutToken = await otherDevice.SyncAsync();
        otherDevice.Token = olderToken;
        var withOlderToken = await otherDevice.SyncAsync();

        Assert.True(withoutToken.CoversAllScopes);
        var block = Assert.Single(withoutToken.Scopes);
        Assert.True(block.FullState);
        Assert.Equal(noteIds.OrderBy(noteId => noteId.ToString(), StringComparer.Ordinal), block.Records.Select(record => record.Id));
        Assert.False(withOlderToken.CoversAllScopes);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task Sync_DeletionsAboveThreshold_OtherDeviceGetsFullState(int deletions, bool expectedFullState)
    {
        const int recordLimit = 3;
        await using var api = await ApiHost.StartAsync(
            fixture.CreateDatabaseConnectionString(),
            new Dictionary<string, string?> { ["Sync:DataTypes:testNote:MaxRecordsPerScope"] = $"{recordLimit}" },
            testDataTypes: true
        );
        var writer = await SyncTestDevice.SignInAsync(api);
        var reader = writer.OtherDevice();
        reader.Token = writer.Token;
        var now = api.TimeProvider.GetUtcNow();
        var noteIds = Enumerable.Range(0, deletions).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var chunk in noteIds.Chunk(recordLimit))
        {
            var written = await writer.SyncAsync([
                .. chunk.Select(noteId => writer.CreateNote(noteId, now, "note")),
                .. chunk.Select(noteId => writer.DeleteNote(noteId, now)),
            ]);
            Assert.All(written.Results, result => Assert.Equal("applied", result.Status));
        }

        var response = await reader.SyncAsync();

        var block = Assert.Single(response.Scopes);
        Assert.Equal(expectedFullState, block.FullState);
        Assert.Empty(block.Records);
        IEnumerable<SyncDeletedRecord> expectedDeletions = expectedFullState
            ? []
            : noteIds.Select(noteId => new SyncDeletedRecord(TestNoteType.DataTypeName, noteId));
        Assert.Equal(expectedDeletions, block.DeletedRecords);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public async Task Sync_DeletionsAboveThreshold_OtherMemberGetsFullState(int deletions, bool expectedFullState)
    {
        const int cardLimit = 3;
        var connectionString = fixture.CreateDatabaseConnectionString();
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:DataTypes:testCard:MaxRecordsPerScope"] = $"{cardLimit}" },
            testDataTypes: true
        );
        var owner = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        await owner.ApplyAllAsync(owner.CreateBoard(boardId, now.AddSeconds(-30), "board"));
        var member = await SyncTestDevice.SignInAsync(api);
        await new DatabaseAdministration(connectionString).InsertMemberEntryAsync(boardId, member.AccountId, now.AddMilliseconds(1));
        await member.SyncAllAsync();
        var cardIds = Enumerable.Range(0, deletions).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var chunk in cardIds.Chunk(cardLimit))
        {
            await owner.ApplyAllAsync([
                .. chunk.Select(cardId => owner.CreateCard(boardId, cardId, now, "card")),
                .. chunk.Select(cardId => owner.DeleteCard(boardId, cardId, now)),
            ]);
        }

        var response = await member.SyncAllAsync();

        var block = Assert.Single(response.Scopes, block => block.ScopeId == boardId);
        Assert.Equal(expectedFullState, block.FullState);
        Guid[] expectedRecordIds = expectedFullState ? [boardId] : [];
        Assert.Equal(expectedRecordIds, block.Records.Select(record => record.Id));
        IEnumerable<SyncDeletedRecord> expectedDeletions = expectedFullState
            ? []
            : cardIds.Select(cardId => new SyncDeletedRecord(TestCardType.DataTypeName, cardId));
        Assert.Equal(expectedDeletions, block.DeletedRecords);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sync_ManyWritersAndContinuousReader_ReaderMissesNoChange(bool sharedScope)
    {
        var reader = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var readerCopy = new Dictionary<Guid, JsonElement>();
        var scopeId = reader.PersonalScopeId;
        if (sharedScope)
        {
            scopeId = Guid.NewGuid();
            Apply(readerCopy, await reader.SyncAllAsync(reader.CreateBoard(scopeId, now, "board")), scopeId);
        }

        var writing = Task.WhenAll(Enumerable.Range(0, 10).Select(_ => WriteRoundsAsync(reader.OtherDevice(), now, scopeId)));
        while (!writing.IsCompleted)
        {
            Apply(readerCopy, await ReadAsync(reader), scopeId);
        }

        await writing;
        Apply(readerCopy, await ReadAsync(reader), scopeId);

        var expectedRecords = Assert.Single((await ReadAsync(reader.OtherDevice())).Scopes, block => block.ScopeId == scopeId).Records;
        Assert.Equal(expectedRecords.Select(record => record.Id).Order(), readerCopy.Keys.Order());
        Assert.All(expectedRecords, record => Assert.True(JsonElement.DeepEquals(record.Fields, readerCopy[record.Id])));

        Task<SyncResponse> ReadAsync(SyncTestDevice device) => sharedScope ? device.SyncAllAsync() : device.SyncAsync();
    }

    [Fact]
    public async Task Sync_ServerWrite_ReachesDevicesLikeCommand()
    {
        var (owner, boardId, cardId) = await SignInWithBoardAndCardAsync(fixture.Api);
        var reader = owner.OtherDevice();
        await reader.SyncAllAsync();
        var initialVersion = (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))!.Version;
        var newCardId = Guid.NewGuid();

        var doneResult = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.System,
                SyncCommandKind.SetFields,
                boardId,
                TestCardType.DataTypeName,
                cardId,
                SyncServerFields.Of(("done", true))
            )
        );
        var afterDone = await reader.SyncAllAsync();
        var createResult = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.OnBehalfOf(owner.AccountId),
                SyncCommandKind.Create,
                boardId,
                TestCardType.DataTypeName,
                newCardId,
                SyncServerFields.Of(("text", "bread"))
            )
        );
        var deleteResult = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(Guid.NewGuid(), SyncActor.System, SyncCommandKind.Delete, boardId, TestCardType.DataTypeName, cardId)
        );
        var afterCreateAndDelete = await reader.SyncAllAsync();

        Assert.Equal(AppliedResult, doneResult);
        var doneBlock = Assert.Single(afterDone.Scopes, block => block.ScopeId == boardId);
        Assert.False(doneBlock.FullState);
        var doneCard = Assert.Single(doneBlock.Records);
        Assert.Equal((cardId, true), (doneCard.Id, doneCard.Fields.GetProperty("done").GetBoolean()));
        Assert.Empty(doneBlock.DeletedRecords);
        Assert.Equal(AppliedResult, createResult);
        Assert.Equal(AppliedResult, deleteResult);
        var block = Assert.Single(afterCreateAndDelete.Scopes, block => block.ScopeId == boardId);
        Assert.False(block.FullState);
        var newCard = Assert.Single(block.Records);
        Assert.Equal((newCardId, "bread"), (newCard.Id, newCard.Fields.GetProperty("text").GetString()));
        Assert.Equal([new SyncDeletedRecord(TestCardType.DataTypeName, cardId)], block.DeletedRecords);
        Assert.Equal(initialVersion + 3, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))!.Version);
    }

    [Fact]
    public async Task WriteAsync_SystemDeleteInDeletedScope_ReturnsTargetDeletedWithoutClaim()
    {
        var (owner, boardId, cardId) = await SignInWithBoardAndCardAsync(fixture.Api);
        await owner.ApplyAllAsync(owner.DeleteBoard(boardId, fixture.Api.TimeProvider.GetUtcNow()));
        var commandId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(commandId, SyncActor.System, SyncCommandKind.Delete, boardId, TestCardType.DataTypeName, cardId)
        );

        Assert.Equal(new SyncServerWriteResult("ignored", null, "targetDeleted"), result);
        Assert.Empty(await ReadProcessedCommandsByIdAsync(fixture.Api, commandId));
    }

    [Fact]
    public async Task WriteAsync_OnBehalfOfRootCreate_CreatesScopeWithOwnerEntry()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var boardId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                commandId,
                SyncActor.OnBehalfOf(device.AccountId),
                SyncCommandKind.Create,
                boardId,
                TestBoardType.DataTypeName,
                boardId,
                SyncServerFields.Of(("title", "server board"))
            )
        );

        Assert.Equal(AppliedResult, result);
        var scope = Assert.IsType<SyncScope>(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
        Assert.Equal((device.AccountId, TestBoardType.ScopeKindName, 1L), (scope.OwnerAccountId, scope.Kind, scope.Version));
        var ownerEntry = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId));
        Assert.Equal(("owner", SyncMemberState.Active, (Guid?)device.AccountId), (ownerEntry.Role, ownerEntry.State, ownerEntry.AccountId));
        Assert.Equal(device.AccountId, Assert.Single(await ReadProcessedCommandsByIdAsync(fixture.Api, commandId)).AccountId);
        var block = Assert.Single((await device.SyncAllAsync()).Scopes, block => block.ScopeId == boardId);
        Assert.True(block.FullState);
        var board = Assert.Single(block.Records);
        Assert.Equal((boardId, "server board"), (board.Id, board.Fields.GetProperty("title").GetString()));
        Assert.True(Assert.Single(block.Members).IsOwn);
    }

    [Theory]
    [InlineData("unknownDataType")]
    [InlineData("unknownKind")]
    [InlineData("rootRecordIdDiffersFromScopeId")]
    [InlineData("recordIdEqualsScopeId")]
    [InlineData("systemRootCreate")]
    [InlineData("fieldsOnDelete")]
    [InlineData("fieldsMissingOnSetFields")]
    [InlineData("emptySetFields")]
    [InlineData("unknownField")]
    [InlineData("valueInvalid")]
    [InlineData("requiredFieldMissing")]
    [InlineData("filledServerField")]
    [InlineData("memberReference")]
    [InlineData("preconditionOnRootDelete")]
    [InlineData("systemRootDelete")]
    [InlineData("recordIdVersion7")]
    [InlineData("scopeIdVersion7")]
    [InlineData("actorMissing")]
    [InlineData("actorDerived")]
    public async Task WriteAsync_InvalidCommand_RejectedInvalidWithoutClaim(string scenario)
    {
        var (owner, boardId, cardId) = await SignInWithBoardAndCardAsync(fixture.Api);
        var noteId = Guid.NewGuid();
        await owner.ApplyAllAsync(owner.CreateNote(noteId, fixture.Api.TimeProvider.GetUtcNow().AddSeconds(-10), OriginalTitle));
        var ownerEntryId = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId)).Id;
        var boardVersion = (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))!.Version;
        var commandId = Guid.NewGuid();
        var newBoardId = Guid.NewGuid();
        var cardChange = new SyncServerCommand(
            commandId,
            SyncActor.System,
            SyncCommandKind.SetFields,
            boardId,
            TestCardType.DataTypeName,
            cardId,
            SyncServerFields.Of(("text", "x"))
        );
        var noteChange = cardChange with
        {
            ScopeId = owner.PersonalScopeId,
            DataType = TestNoteType.DataTypeName,
            RecordId = noteId,
            Fields = SyncServerFields.Of(("title", "x")),
        };
        var boardDelete = cardChange with
        {
            Kind = SyncCommandKind.Delete,
            DataType = TestBoardType.DataTypeName,
            RecordId = boardId,
            Fields = null,
        };
        var command = scenario switch
        {
            "unknownDataType" => cardChange with { DataType = "unknownType" },
            "unknownKind" => cardChange with { Kind = new SyncCommandKind("move") },
            "rootRecordIdDiffersFromScopeId" => cardChange with
            {
                DataType = TestBoardType.DataTypeName,
                RecordId = Guid.NewGuid(),
                Fields = SyncServerFields.Of(("title", "x")),
            },
            "recordIdEqualsScopeId" => cardChange with { RecordId = boardId },
            "systemRootCreate" => cardChange with
            {
                Kind = SyncCommandKind.Create,
                ScopeId = newBoardId,
                DataType = TestBoardType.DataTypeName,
                RecordId = newBoardId,
                Fields = SyncServerFields.Of(("title", "x")),
            },
            "fieldsOnDelete" => cardChange with { Kind = SyncCommandKind.Delete },
            "fieldsMissingOnSetFields" => cardChange with { Fields = null },
            "emptySetFields" => cardChange with { Fields = SyncServerFields.Of() },
            "unknownField" => cardChange with { Fields = SyncServerFields.Of(("text", "x"), ("unknown", 1)) },
            "valueInvalid" => noteChange with { Fields = SyncServerFields.Of(("title", new string('x', 101))) },
            "requiredFieldMissing" => noteChange with { Kind = SyncCommandKind.Create, RecordId = Guid.NewGuid(), Fields = SyncServerFields.Of() },
            "filledServerField" => cardChange with { Fields = SyncServerFields.Of(("text", "x"), ("createdAt", 1)) },
            "memberReference" => cardChange with { Fields = SyncServerFields.Of(("text", "x"), ("doneBy", ownerEntryId)) },
            "preconditionOnRootDelete" => boardDelete with { Actor = SyncActor.OnBehalfOf(owner.AccountId), Precondition = _ => true },
            "systemRootDelete" => boardDelete,
            "recordIdVersion7" => cardChange with { Kind = SyncCommandKind.Create, RecordId = Guid.CreateVersion7() },
            "scopeIdVersion7" => cardChange with { ScopeId = Guid.CreateVersion7() },
            "actorMissing" => cardChange with { Actor = null! },
            "actorDerived" => cardChange with { Actor = new DerivedActor() },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        var result = await fixture.Api.WriteAsServerAsync(command);

        Assert.Equal(new SyncServerWriteResult("rejected", "invalid", null), result);
        Assert.Empty(await ReadProcessedCommandsByIdAsync(fixture.Api, commandId));
        Assert.Equal("milk", (await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId))?.Text);
        Assert.Equal(OriginalTitle, await TestNoteQueries.ReadTitleAsync(fixture.Api, noteId));
        Assert.Equal(boardVersion, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))?.Version);
    }

    [Theory]
    [InlineData("deleteHolds")]
    [InlineData("deleteFails")]
    [InlineData("setFieldsSeesStoredValue")]
    public async Task WriteAsync_PreconditionDecides_AppliesOnlyWhenItHolds(string scenario)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var noteId = Guid.NewGuid();
        await device.SyncAsync(device.CreateNote(noteId, fixture.Api.TimeProvider.GetUtcNow().AddSeconds(-30), "note"));
        var scopeVersion = (await SharedScopeQueries.ReadScopeAsync(fixture.Api, device.PersonalScopeId))!.Version;
        var delete = new SyncServerCommand(
            Guid.NewGuid(),
            SyncActor.System,
            SyncCommandKind.Delete,
            device.PersonalScopeId,
            TestNoteType.DataTypeName,
            noteId
        );
        (SyncServerCommand Command, SyncServerWriteResult Result, string? Title, long Version) expected = scenario switch
        {
            "deleteHolds" => (delete with { Precondition = record => record is TestNote { Title: "note" } }, AppliedResult, null, scopeVersion + 1),
            "deleteFails" => (
                delete with { Precondition = record => record is TestNote { Title: "other" } },
                new("ignored", null, "lostToNewer"),
                "note",
                scopeVersion
            ),
            "setFieldsSeesStoredValue" => (
                delete with
                {
                    Kind = SyncCommandKind.SetFields,
                    Fields = SyncServerFields.Of(("title", "changed")),
                    Precondition = record => record is TestNote { Title: "note" },
                },
                AppliedResult,
                "changed",
                scopeVersion + 1
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        var result = await fixture.Api.WriteAsServerAsync(expected.Command);

        Assert.Equal(expected.Result, result);
        Assert.Equal(expected.Title, await TestNoteQueries.ReadTitleAsync(fixture.Api, noteId));
        Assert.Equal(expected.Version, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, device.PersonalScopeId))!.Version);
        SyncDeletedRecord[] expectedDeletions = expected.Title is null ? [new(TestNoteType.DataTypeName, noteId)] : [];
        Assert.Equal(expectedDeletions, (await device.SyncAsync()).Scopes.SelectMany(block => block.DeletedRecords));
    }

    [Fact]
    public async Task WriteAsync_NewRecordWithFailingPrecondition_AppliesWithoutEvaluatingIt()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var noteId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.System,
                SyncCommandKind.Create,
                device.PersonalScopeId,
                TestNoteType.DataTypeName,
                noteId,
                SyncServerFields.Of(("title", "server")),
                _ => false
            )
        );

        Assert.Equal(AppliedResult, result);
        Assert.Equal("server", await TestNoteQueries.ReadTitleAsync(fixture.Api, noteId));
    }

    [Fact]
    public async Task WriteAsync_ServerFieldWithoutFill_AppliesWithoutTimeComparison()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        await device.SyncAsync(device.CreateNote(noteId, now, "note"));
        await device.SyncAsync(device.SetNoteFields(noteId, now.AddMinutes(4), SyncTestCommands.TitleFields("device")));
        var pin = new SyncServerCommand(
            Guid.NewGuid(),
            SyncActor.System,
            SyncCommandKind.SetFields,
            device.PersonalScopeId,
            TestNoteType.DataTypeName,
            noteId,
            SyncServerFields.Of(("pinned", true))
        );

        var pinResult = await fixture.Api.WriteAsServerAsync(pin);
        var afterPin = await device.SyncAsync();
        var titleAndUnpinResult = await fixture.Api.WriteAsServerAsync(
            pin with
            {
                CommandId = Guid.NewGuid(),
                Fields = SyncServerFields.Of(("title", "server"), ("pinned", false)),
            }
        );
        var afterUnpin = await device.SyncAsync();

        Assert.Equal(AppliedResult, pinResult);
        var pinnedNote = Assert.Single(Assert.Single(afterPin.Scopes).Records);
        Assert.Equal(
            (noteId, """{"title":"device","color":null,"weight":null,"pinned":true}"""),
            (pinnedNote.Id, pinnedNote.Fields.GetRawText())
        );
        Assert.Equal(AppliedResult, titleAndUnpinResult);
        var unpinnedNote = Assert.Single(Assert.Single(afterUnpin.Scopes).Records);
        Assert.Equal(
            (noteId, """{"title":"device","color":null,"weight":null,"pinned":false}"""),
            (unpinnedNote.Id, unpinnedNote.Fields.GetRawText())
        );
    }

    [Fact]
    public async Task WriteAsync_SystemCreateWithServerFieldWithoutFill_DeviceSeesField()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var noteId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.System,
                SyncCommandKind.Create,
                device.PersonalScopeId,
                TestNoteType.DataTypeName,
                noteId,
                SyncServerFields.Of(("title", "server"), ("pinned", true))
            )
        );
        var response = await device.SyncAsync();

        Assert.Equal(AppliedResult, result);
        var note = Assert.Single(Assert.Single(response.Scopes).Records);
        Assert.Equal((noteId, """{"title":"server","color":null,"weight":null,"pinned":true}"""), (note.Id, note.Fields.GetRawText()));
    }

    [Fact]
    public async Task WriteAsync_SystemWrite_FillsNoPersonAndKeepsChangedBy()
    {
        var (_, boardId, cardId) = await SignInWithBoardAndCardAsync(fixture.Api);
        var ownerEntryId = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId)).Id;
        var newCardId = Guid.NewGuid();
        var change = new SyncServerCommand(
            Guid.NewGuid(),
            SyncActor.System,
            SyncCommandKind.SetFields,
            boardId,
            TestCardType.DataTypeName,
            cardId,
            SyncServerFields.Of(("text", "changed"))
        );

        var changeResult = await fixture.Api.WriteAsServerAsync(change);
        var createResult = await fixture.Api.WriteAsServerAsync(
            change with
            {
                CommandId = Guid.NewGuid(),
                Kind = SyncCommandKind.Create,
                RecordId = newCardId,
                Fields = SyncServerFields.Of(("text", "server")),
            }
        );

        Assert.Equal(AppliedResult, changeResult);
        var changedCard = Assert.IsType<TestCard>(await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId));
        Assert.Equal(
            ("changed", (Guid?)ownerEntryId, (Guid?)ownerEntryId),
            (changedCard.Text, changedCard.ChangedByMemberId, changedCard.CreatedByMemberId)
        );
        Assert.Equal(AppliedResult, createResult);
        var createdCard = Assert.IsType<TestCard>(await SharedScopeQueries.ReadCardAsync(fixture.Api, newCardId));
        Assert.Equal(
            ((Guid?)null, (Guid?)null, DateTimeOffset.FromUnixTimeMilliseconds(fixture.Api.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds())),
            (createdCard.ChangedByMemberId, createdCard.CreatedByMemberId, createdCard.CreatedAt)
        );
    }

    [Fact]
    public async Task WriteAsync_OnBehalfOfFormerMember_FillsFormerEntryAndClaimsForAccount()
    {
        var (_, boardId, cardId) = await SignInWithBoardAndCardAsync(fixture.Api);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        var formerEntryId = await new DatabaseAdministration(fixture.ConnectionString).InsertMemberEntryAsync(
            boardId,
            member.AccountId,
            fixture.Api.TimeProvider.GetUtcNow(),
            "member",
            "former"
        );
        var commandId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                commandId,
                SyncActor.OnBehalfOf(member.AccountId),
                SyncCommandKind.SetFields,
                boardId,
                TestCardType.DataTypeName,
                cardId,
                SyncServerFields.Of(("done", true))
            )
        );

        Assert.Equal(AppliedResult, result);
        var card = Assert.IsType<TestCard>(await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId));
        Assert.Equal(((Guid?)formerEntryId, (Guid?)formerEntryId), (card.ChangedByMemberId, card.DoneByMemberId));
        Assert.Equal(member.AccountId, Assert.Single(await ReadProcessedCommandsByIdAsync(fixture.Api, commandId)).AccountId);
    }

    [Theory]
    [InlineData("memberDeletesCard")]
    [InlineData("accountWithoutEntry")]
    [InlineData("otherAccountsPersonalScope")]
    public async Task WriteAsync_OnBehalfOfWithoutAccessOrRole_AppliesForThatAccount(string scenario)
    {
        var (owner, boardId, cardId) = await SignInWithBoardAndCardAsync(fixture.Api);
        var ownerEntryId = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(fixture.Api, boardId)).Id;
        var actor = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        await owner.SyncAsync(owner.CreateNote(noteId, now.AddSeconds(-10), OriginalTitle));
        if (scenario == "memberDeletesCard")
        {
            await new DatabaseAdministration(fixture.ConnectionString).InsertMemberEntryAsync(boardId, actor.AccountId, now);
        }

        var cardChange = new SyncServerCommand(
            Guid.NewGuid(),
            SyncActor.OnBehalfOf(actor.AccountId),
            SyncCommandKind.SetFields,
            boardId,
            TestCardType.DataTypeName,
            cardId,
            SyncServerFields.Of(("done", true))
        );
        (SyncServerCommand Command, (bool Done, Guid? ChangedBy, Guid? DoneBy)? Card, string Title) expected = scenario switch
        {
            "memberDeletesCard" => (cardChange with { Kind = SyncCommandKind.Delete, Fields = null }, null, OriginalTitle),
            "accountWithoutEntry" => (cardChange, (true, null, null), OriginalTitle),
            "otherAccountsPersonalScope" => (
                cardChange with
                {
                    ScopeId = owner.PersonalScopeId,
                    DataType = TestNoteType.DataTypeName,
                    RecordId = noteId,
                    Fields = SyncServerFields.Of(("title", "server")),
                },
                (false, ownerEntryId, null),
                "server"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        var result = await fixture.Api.WriteAsServerAsync(expected.Command);

        Assert.Equal(AppliedResult, result);
        var card = await SharedScopeQueries.ReadCardAsync(fixture.Api, cardId);
        Assert.Equal(expected.Card, card is null ? null : (card.Done, card.ChangedByMemberId, card.DoneByMemberId));
        Assert.Equal(expected.Title, await TestNoteQueries.ReadTitleAsync(fixture.Api, noteId));
        Assert.Equal(actor.AccountId, Assert.Single(await ReadProcessedCommandsByIdAsync(fixture.Api, expected.Command.CommandId)).AccountId);
    }

    [Fact]
    public async Task WriteAsync_OnBehalfOfWriteInDeletedScope_ReturnsNoAccessAndClaims()
    {
        var (owner, boardId, cardId) = await SignInWithBoardAndCardAsync(fixture.Api);
        await owner.ApplyAllAsync(owner.DeleteBoard(boardId, fixture.Api.TimeProvider.GetUtcNow()));
        var commandId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                commandId,
                SyncActor.OnBehalfOf(owner.AccountId),
                SyncCommandKind.SetFields,
                boardId,
                TestCardType.DataTypeName,
                cardId,
                SyncServerFields.Of(("done", true))
            )
        );

        Assert.Equal(new SyncServerWriteResult("rejected", "noAccess", null), result);
        Assert.Equal(owner.AccountId, Assert.Single(await ReadProcessedCommandsByIdAsync(fixture.Api, commandId)).AccountId);
    }

    [Fact]
    public async Task WriteAsync_SameCommandIdTwice_AppliesOnceAndReturnsStoredResult()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var noteId = Guid.NewGuid();
        await device.SyncAsync(device.CreateNote(noteId, fixture.Api.TimeProvider.GetUtcNow().AddSeconds(-10), OriginalTitle));
        var scopeVersion = (await SharedScopeQueries.ReadScopeAsync(fixture.Api, device.PersonalScopeId))!.Version;
        var first = new SyncServerCommand(
            Guid.NewGuid(),
            SyncActor.System,
            SyncCommandKind.SetFields,
            device.PersonalScopeId,
            TestNoteType.DataTypeName,
            noteId,
            SyncServerFields.Of(("title", "first"))
        );

        var firstResult = await fixture.Api.WriteAsServerAsync(first);
        var secondResult = await fixture.Api.WriteAsServerAsync(first with { Fields = SyncServerFields.Of(("title", "second")) });

        Assert.Equal(AppliedResult, firstResult);
        Assert.Equal(AppliedResult, secondResult);
        Assert.Equal("first", await TestNoteQueries.ReadTitleAsync(fixture.Api, noteId));
        Assert.Equal(scopeVersion + 1, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, device.PersonalScopeId))!.Version);
    }

    [Fact]
    public async Task WriteAsync_RuleThrows_ReturnsFailedAndLogsCommandFailed()
    {
        var logRecords = new List<LogRecord>();
        var commandId = Guid.NewGuid();
        await using (var api = await ApiHost.StartAsync(fixture.CreateDatabaseConnectionString(), logRecords: logRecords, testDataTypes: true))
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var noteId = Guid.NewGuid();
            await device.SyncAsync(device.CreateNote(noteId, api.TimeProvider.GetUtcNow().AddSeconds(-10), OriginalTitle));
            await using var scope = api.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MurmliDbContext>();

            var result = await scope
                .ServiceProvider.GetRequiredService<SyncServerWriter>()
                .WriteAsync(
                    new SyncServerCommand(
                        commandId,
                        SyncActor.System,
                        SyncCommandKind.SetFields,
                        device.PersonalScopeId,
                        TestNoteType.DataTypeName,
                        noteId,
                        SyncServerFields.Of(("title", TestNoteType.FailingTitle))
                    ),
                    TestContext.Current.CancellationToken
                );

            Assert.Equal(new SyncServerWriteResult("failed", null, null), result);
            Assert.Empty(context.ChangeTracker.Entries());
            Assert.Equal(OriginalTitle, await TestNoteQueries.ReadTitleAsync(api, noteId));
            Assert.Empty(await ReadProcessedCommandsByIdAsync(api, commandId));
        }

        var failure = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == "SyncCommandFailed");
        Assert.Equal(LogLevel.Error, failure.LogLevel);
        Assert.Equal("The test note rule fails on purpose.", Assert.IsType<InvalidOperationException>(failure.Exception).Message);
        var attributes = failure.Attributes!.ToDictionary();
        Assert.Equal(commandId, attributes["CommandId"]);
        Assert.Equal("setFields", attributes["CommandKind"]);
        Assert.Equal(TestNoteType.DataTypeName, attributes["DataType"]);
        Assert.DoesNotContain("DeviceId", attributes.Keys);
    }

    [Fact]
    public async Task WriteAsync_OwnerDeletedWhileWriteWaits_ReturnsTargetDeleted()
    {
        var logRecords = new List<LogRecord>();
        var connectionString = fixture.CreateDatabaseConnectionString();
        await using (var api = await ApiHost.StartAsync(connectionString, logRecords: logRecords, testDataTypes: true))
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var noteId = Guid.NewGuid();
            await device.SyncAsync(device.CreateNote(noteId, api.TimeProvider.GetUtcNow().AddSeconds(-10), OriginalTitle));
            var administration = new DatabaseAdministration(connectionString);

            await using var deletion = await administration.BeginUserDeletionAsync(device.AccountId);
            var write = api.WriteAsServerAsync(
                new SyncServerCommand(
                    Guid.NewGuid(),
                    SyncActor.System,
                    SyncCommandKind.SetFields,
                    device.PersonalScopeId,
                    TestNoteType.DataTypeName,
                    noteId,
                    SyncServerFields.Of(("title", "late"))
                )
            );
            await administration.WaitForLockWaitersAsync(1);
            await deletion.CommitAsync();
            var result = await write;

            Assert.Equal(new SyncServerWriteResult("ignored", null, "targetDeleted"), result);
        }

        Assert.DoesNotContain(logRecords, logRecord => logRecord.EventId.Name == "SyncCommandFailed");
    }

    [Fact]
    public async Task WriteAsync_ScopeLockedPastLockTimeout_ThrowsRetryLimitExceeded()
    {
        var connectionString = fixture.CreateDatabaseConnectionString();
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
            testDataTypes: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var noteId = Guid.NewGuid();
        await device.SyncAsync(device.CreateNote(noteId, api.TimeProvider.GetUtcNow().AddSeconds(-10), OriginalTitle));
        var change = new SyncServerCommand(
            Guid.NewGuid(),
            SyncActor.System,
            SyncCommandKind.SetFields,
            device.PersonalScopeId,
            TestNoteType.DataTypeName,
            noteId,
            SyncServerFields.Of(("title", "locked"))
        );

        await using (await new DatabaseAdministration(connectionString).LockScopeRowAsync(device.PersonalScopeId))
        {
            await Assert.ThrowsAsync<RetryLimitExceededException>(() => api.WriteAsServerAsync(change));
        }

        Assert.Equal(OriginalTitle, await TestNoteQueries.ReadTitleAsync(api, noteId));
    }

    [Fact]
    public async Task WriteAsync_CancelledWhileWaitingForScopeLock_ThrowsWithoutCommandFailed()
    {
        var logRecords = new List<LogRecord>();
        var connectionString = fixture.CreateDatabaseConnectionString();
        await using (var api = await ApiHost.StartAsync(connectionString, logRecords: logRecords, testDataTypes: true))
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var noteId = Guid.NewGuid();
            await device.SyncAsync(device.CreateNote(noteId, api.TimeProvider.GetUtcNow().AddSeconds(-10), OriginalTitle));
            var administration = new DatabaseAdministration(connectionString);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            await using var scope = api.Services.CreateAsyncScope();

            await using (await administration.LockScopeRowAsync(device.PersonalScopeId))
            {
                var write = scope
                    .ServiceProvider.GetRequiredService<SyncServerWriter>()
                    .WriteAsync(
                        new SyncServerCommand(
                            Guid.NewGuid(),
                            SyncActor.System,
                            SyncCommandKind.SetFields,
                            device.PersonalScopeId,
                            TestNoteType.DataTypeName,
                            noteId,
                            SyncServerFields.Of(("title", "cancelled"))
                        ),
                        cancellation.Token
                    );
                await administration.WaitForLockWaitersAsync(1);
                await cancellation.CancelAsync();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
            }

            Assert.Equal(OriginalTitle, await TestNoteQueries.ReadTitleAsync(api, noteId));
        }

        Assert.DoesNotContain(logRecords, logRecord => logRecord.EventId.Name == "SyncCommandFailed");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteAsync_OnBehalfOfRootDelete_DeletesOnlyForOwner(bool deleterIsOwner)
    {
        var (owner, boardId, _) = await SignInWithBoardAndCardAsync(fixture.Api);
        var member = await SyncTestDevice.SignInAsync(fixture.Api);
        await new DatabaseAdministration(fixture.ConnectionString).InsertMemberEntryAsync(
            boardId,
            member.AccountId,
            fixture.Api.TimeProvider.GetUtcNow()
        );
        var boardVersion = (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))!.Version;
        var deleter = deleterIsOwner ? owner : member;
        var commandId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                commandId,
                SyncActor.OnBehalfOf(deleter.AccountId),
                SyncCommandKind.Delete,
                boardId,
                TestBoardType.DataTypeName,
                boardId
            )
        );

        Assert.Equal(deleterIsOwner ? AppliedResult : new SyncServerWriteResult("rejected", "noAccess", null), result);
        Assert.Equal(deleterIsOwner ? null : (long?)boardVersion, (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))?.Version);
        Assert.Equal(deleter.AccountId, Assert.Single(await ReadProcessedCommandsByIdAsync(fixture.Api, commandId)).AccountId);
        SyncRemovedScope[] expectedRemovedScopes = deleterIsOwner ? [new(boardId, true)] : [];
        Assert.Equal(expectedRemovedScopes, (await owner.SyncAllAsync()).RemovedScopes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteAsync_OnBehalfOfRootCreateOfExistingScope_ChangesOnlyForOwner(bool writerIsOwner)
    {
        var (owner, boardId, _) = await SignInWithBoardAndCardAsync(fixture.Api);
        var writer = writerIsOwner ? owner : await SyncTestDevice.SignInAsync(fixture.Api);
        var boardVersion = (await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId))!.Version;
        var commandId = Guid.NewGuid();

        var result = await fixture.Api.WriteAsServerAsync(
            new SyncServerCommand(
                commandId,
                SyncActor.OnBehalfOf(writer.AccountId),
                SyncCommandKind.Create,
                boardId,
                TestBoardType.DataTypeName,
                boardId,
                SyncServerFields.Of(("title", "server board"))
            )
        );

        Assert.Equal(writerIsOwner ? AppliedResult : new SyncServerWriteResult("rejected", "noAccess", null), result);
        var scope = Assert.IsType<SyncScope>(await SharedScopeQueries.ReadScopeAsync(fixture.Api, boardId));
        Assert.Equal(
            writerIsOwner ? ("server board", owner.AccountId, boardVersion + 1) : ("board", owner.AccountId, boardVersion),
            (await SharedScopeQueries.ReadBoardTitleAsync(fixture.Api, boardId), scope.OwnerAccountId, scope.Version)
        );
        Assert.Equal(writer.AccountId, Assert.Single(await ReadProcessedCommandsByIdAsync(fixture.Api, commandId)).AccountId);
    }

    [Fact]
    public async Task WriteAsync_ScopeDeletedWhileSystemWriteWaits_ReturnsTargetDeleted()
    {
        var connectionString = fixture.CreateDatabaseConnectionString();
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:30" },
            testDataTypes: true
        );
        var (owner, boardId, cardId) = await SignInWithBoardAndCardAsync(api);
        var administration = new DatabaseAdministration(connectionString);
        var delete = owner.DeleteBoard(boardId, api.TimeProvider.GetUtcNow());
        var commandId = Guid.NewGuid();
        Task<SyncResponse> deletion;
        Task<SyncServerWriteResult> write;
        await using (await administration.LockScopeRowAsync(boardId))
        {
            deletion = owner.SyncAllAsync(delete);
            await administration.WaitForLockWaitersAsync(1);
            write = api.WriteAsServerAsync(
                new SyncServerCommand(
                    commandId,
                    SyncActor.System,
                    SyncCommandKind.SetFields,
                    boardId,
                    TestCardType.DataTypeName,
                    cardId,
                    SyncServerFields.Of(("done", true))
                )
            );
            await administration.WaitForLockWaitersAsync(2);
        }

        var deletionResponse = await deletion;
        var result = await write;

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(delete), "applied"), Assert.Single(deletionResponse.Results));
        Assert.Equal(new SyncServerWriteResult("ignored", null, "targetDeleted"), result);
        var processedCommand = Assert.Single(await ReadProcessedCommandsByIdAsync(api, commandId));
        Assert.Equal(
            (owner.AccountId, SyncResultStatus.Ignored, null, "targetDeleted"),
            (processedCommand.AccountId, processedCommand.Status, processedCommand.Reason, processedCommand.Cause)
        );
    }

    private static async Task WriteRoundsAsync(SyncTestDevice writer, DateTimeOffset now, Guid scopeId)
    {
        var isPersonalScope = scopeId == writer.PersonalScopeId;
        var recordIds = new List<Guid>();
        for (var round = 1; round <= 4; round++)
        {
            var recordId = Guid.NewGuid();
            List<JsonObject> commands =
            [
                isPersonalScope
                    ? writer.CreateNote(recordId, now, $"note of round {round}")
                    : writer.CreateCard(scopeId, recordId, now, $"card of round {round}"),
            ];
            if (recordIds.Count > 0)
            {
                var changedText = $"changed in round {round}";
                commands.Add(
                    isPersonalScope
                        ? writer.SetNoteFields(recordIds[^1], now, SyncTestCommands.TitleFields(changedText))
                        : writer.SetCardFields(scopeId, recordIds[^1], now, new JsonObject { ["text"] = changedText })
                );
            }

            if (round == 3)
            {
                commands.Add(isPersonalScope ? writer.DeleteNote(recordIds[0], now) : writer.DeleteCard(scopeId, recordIds[0], now));
            }

            recordIds.Add(recordId);
            var response = isPersonalScope
                ? await writer.SyncAsync([.. commands])
                : await writer.SyncAsync(SyncTestDevice.AllDataTypes, [.. commands]);
            Assert.All(response.Results, result => Assert.Equal("applied", result.Status));
        }
    }

    private static async Task<(SyncTestDevice Owner, Guid BoardId, Guid CardId)> SignInWithBoardAndCardAsync(ApiHost api)
    {
        var owner = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await owner.ApplyAllAsync(
            owner.CreateBoard(boardId, now.AddSeconds(-30), "board"),
            owner.CreateCard(boardId, cardId, now.AddSeconds(-20), "milk")
        );
        return (owner, boardId, cardId);
    }

    private static Task<List<ProcessedSyncCommand>> ReadProcessedCommandsByIdAsync(ApiHost api, Guid commandId) =>
        api.QueryDatabaseAsync(context =>
            context
                .Set<ProcessedSyncCommand>()
                .AsNoTracking()
                .Where(command => command.CommandId == commandId)
                .ToListAsync(TestContext.Current.CancellationToken)
        );

    private static void Apply(Dictionary<Guid, JsonElement> copy, SyncResponse response, Guid scopeId)
    {
        foreach (var block in response.Scopes.Where(block => block.ScopeId == scopeId))
        {
            if (block.FullState)
            {
                copy.Clear();
            }

            foreach (var record in block.Records)
            {
                copy[record.Id] = record.Fields;
            }

            foreach (var deletion in block.DeletedRecords)
            {
                copy.Remove(deletion.Id);
            }
        }
    }

    private async Task<string> ReadCurrentTokenAsync(Guid scopeId)
    {
        var databaseIdentity = await new DatabaseAdministration(fixture.ConnectionString).ReadDatabaseIdentityAsync();
        var scope = await fixture.Api.QueryDatabaseAsync(context =>
            context.Set<SyncScope>().AsNoTracking().SingleAsync(scope => scope.Id == scopeId, TestContext.Current.CancellationToken)
        );
        return SyncTokenFormat.Encode(databaseIdentity, [(scope.Id, scope.Epoch, scope.Version)]);
    }

    private sealed record DerivedActor() : SyncActor(SyncActor.System);
}
