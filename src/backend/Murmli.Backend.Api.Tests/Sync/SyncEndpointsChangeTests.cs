using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsChangeTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    private const string OriginalTitle = "original";

    private const string OriginalFields = """{"title":"original","color":null,"weight":null,"pinned":false}""";

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
                await ChangeOnOtherDeviceAsync(device.CreateNote(otherNoteId, now, "b"), device.DeleteNote(noteId, now));
                break;
            case "tombstoneFloorAtTokenVersion":
                await ChangeOnOtherDeviceAsync(device.CreateNote(otherNoteId, now, "b"));
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

        async Task ChangeOnOtherDeviceAsync(params JsonObject[] commands)
        {
            var changed = await device.OtherDevice().SyncAsync(commands);
            Assert.All(changed.Results, result => Assert.Equal("applied", result.Status));
        }
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

    [Fact]
    public async Task Sync_ManyWritersAndContinuousReader_ReaderMissesNoChange()
    {
        var reader = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var readerCopy = new Dictionary<Guid, JsonElement>();

        var writing = Task.WhenAll(Enumerable.Range(0, 10).Select(_ => WriteRoundsAsync(reader.OtherDevice(), now)));
        while (!writing.IsCompleted)
        {
            Apply(readerCopy, await reader.SyncAsync());
        }

        await writing;
        Apply(readerCopy, await reader.SyncAsync());

        var expectedRecords = Assert.Single((await reader.OtherDevice().SyncAsync()).Scopes).Records;
        Assert.Equal(expectedRecords.Select(record => record.Id).Order(), readerCopy.Keys.Order());
        Assert.All(expectedRecords, record => Assert.True(JsonElement.DeepEquals(record.Fields, readerCopy[record.Id])));
    }

    private static async Task WriteRoundsAsync(SyncTestDevice writer, DateTimeOffset now)
    {
        var noteIds = new List<Guid>();
        for (var round = 1; round <= 4; round++)
        {
            var noteId = Guid.NewGuid();
            List<JsonObject> commands = [writer.CreateNote(noteId, now, $"note of round {round}")];
            if (noteIds.Count > 0)
            {
                commands.Add(writer.SetNoteFields(noteIds[^1], now, SyncTestCommands.TitleFields($"changed in round {round}")));
            }

            if (round == 3)
            {
                commands.Add(writer.DeleteNote(noteIds[0], now));
            }

            noteIds.Add(noteId);
            var response = await writer.SyncAsync([.. commands]);
            Assert.All(response.Results, result => Assert.Equal("applied", result.Status));
        }
    }

    private static void Apply(Dictionary<Guid, JsonElement> copy, SyncResponse response)
    {
        foreach (var block in response.Scopes)
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
}
