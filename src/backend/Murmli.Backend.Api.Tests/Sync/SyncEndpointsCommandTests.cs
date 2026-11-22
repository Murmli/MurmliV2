using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;
using OpenTelemetry.Logs;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsCommandTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    private const long MillisecondsPerMinute = 60_000;

    private const long MillisecondsPerDay = 86_400_000;

    private const long RetentionMilliseconds = 45 * MillisecondsPerDay;

    private const long ClockAheadToleranceMilliseconds = 5 * MillisecondsPerMinute;

    private const string PlaceholderTitle = "placeholder";

    private const string LoneSurrogateJson = "\"\\ud800\"";

    private static readonly SyncOriginalResult AppliedOriginal = new("applied");

    [Fact]
    public async Task Sync_ResentCommand_AppliesOnceAndReturnsOriginalResult()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var noteId = Guid.NewGuid();
        var create = device.CreateNote(noteId, fixture.Api.TimeProvider.GetUtcNow(), "note");
        Assert.Equal("applied", Assert.Single((await device.SyncAsync(create)).Results).Status);
        var versionBeforeResend = ReadPersonalScopeVersion(device.Token!);

        var response = await device.SyncAsync(create);

        Assert.Equal(
            new SyncCommandResult(SyncTestCommands.GetId(create), "alreadyProcessed", Original: AppliedOriginal),
            Assert.Single(response.Results)
        );
        var block = Assert.Single(response.Scopes);
        Assert.False(block.FullState);
        var record = Assert.Single(block.Records);
        Assert.Equal(
            (noteId, """{"title":"note","color":null,"weight":null,"pinned":false}"""),
            (record.Id, record.Fields.GetRawText())
        );
        Assert.Equal(versionBeforeResend, ReadPersonalScopeVersion(response.Token));
    }

    [Fact]
    public async Task Sync_ResentCommandWithOtherTarget_IncludesOnlyStoredTargetState()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var firstNoteId = Guid.NewGuid();
        var secondNoteId = Guid.NewGuid();
        var firstCreate = device.CreateNote(firstNoteId, now.AddSeconds(-10), "first");
        await device.SyncAsync(firstCreate, device.CreateNote(secondNoteId, now.AddSeconds(-10), "second"));
        var changeWithReusedId = SyncTestCommands.With(
            device.SetNoteFields(secondNoteId, now, SyncTestCommands.TitleFields("changed")),
            "id",
            SyncTestCommands.GetId(firstCreate)
        );

        var response = await device.SyncAsync(changeWithReusedId);

        Assert.Equal(
            new SyncCommandResult(SyncTestCommands.GetId(firstCreate), "alreadyProcessed", Original: AppliedOriginal),
            Assert.Single(response.Results)
        );
        var block = Assert.Single(response.Scopes);
        Assert.Equal(firstNoteId, Assert.Single(block.Records).Id);
        Assert.Empty(block.DeletedRecords);
        Assert.Equal("second", await TestNoteQueries.ReadTitleAsync(fixture.Api, secondNoteId));
    }

    [Fact]
    public async Task Sync_SameCommandIdFromTwoAccounts_ProcessesBoth()
    {
        var first = await SyncTestDevice.SignInAsync(fixture.Api);
        var second = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var firstNoteId = Guid.NewGuid();
        var secondNoteId = Guid.NewGuid();
        var firstCreate = first.CreateNote(firstNoteId, now, "first");
        var commandId = SyncTestCommands.GetId(firstCreate);
        var secondCreate = SyncTestCommands.With(second.CreateNote(secondNoteId, now, "second"), "id", commandId);

        var firstResponse = await first.SyncAsync(firstCreate);
        var secondResponse = await second.SyncAsync(secondCreate);

        Assert.Equal(new SyncCommandResult(commandId, "applied"), Assert.Single(firstResponse.Results));
        Assert.Equal(new SyncCommandResult(commandId, "applied"), Assert.Single(secondResponse.Results));
        Assert.Equal("first", await TestNoteQueries.ReadTitleAsync(fixture.Api, firstNoteId));
        Assert.Equal("second", await TestNoteQueries.ReadTitleAsync(fixture.Api, secondNoteId));
    }

    [Theory]
    [InlineData("titleTooLong")]
    [InlineData("titleTooLongInUtf16Units")]
    [InlineData("unknownField")]
    [InlineData("partialGroup")]
    [InlineData("loneSurrogateInTitle")]
    [InlineData("nullCharacterInTitle")]
    [InlineData("timeInMicroseconds")]
    [InlineData("serverField")]
    [InlineData("unknownKey")]
    [InlineData("weightAtExclusiveMinimum")]
    [InlineData("weightAboveMaximum")]
    [InlineData("weightWithTooManyDecimals")]
    [InlineData("fieldsOnDelete")]
    [InlineData("emptyFieldsOnSetFields")]
    [InlineData("createWithoutTitle")]
    [InlineData("unknownCommandMember")]
    [InlineData("unknownKind")]
    [InlineData("unregisteredDataType")]
    [InlineData("recordIdEqualsScopeId")]
    [InlineData("duplicateTitle")]
    [InlineData("titleAsNumber")]
    [InlineData("nullCharacterInKind")]
    [InlineData("loneSurrogateInDataType")]
    public async Task Sync_InvalidCommandAmongValid_RejectsOnlyThatCommand(string invalidity)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var firstNoteId = Guid.NewGuid();
        var lastNoteId = Guid.NewGuid();
        var firstCreate = device.CreateNote(firstNoteId, now, "first").ToJsonString();
        var lastCreate = device.CreateNote(lastNoteId, now, "last").ToJsonString();

        var response = await device.SyncRawAsync($"[{firstCreate},{CreateInvalidCommand(device, now, invalidity)},{lastCreate}]");

        Assert.Equal(
            new (string, string?)[] { ("applied", null), ("rejected", "invalid"), ("applied", null) },
            response.Results.Select(result => (result.Status, result.Reason))
        );
        Assert.Equal(new[] { firstNoteId, lastNoteId }.Order(), (await ReadNoteIdsAsync(device.PersonalScopeId)).Order());
    }

    [Fact]
    public async Task Sync_CommandForUnlistedDataType_RejectedInvalid()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var create = device.CreateNote(Guid.NewGuid(), fixture.Api.TimeProvider.GetUtcNow(), "note");

        var response = await device.SyncAsync(dataTypes: [], commands: [create]);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "rejected", "invalid"), Assert.Single(response.Results));
        Assert.Empty(response.Scopes);
        Assert.Equal(0, await TestNoteQueries.CountInScopeAsync(fixture.Api, device.PersonalScopeId));
        var stored = Assert.Single(
            await TestNoteQueries.ReadProcessedCommandsAsync(fixture.Api, device.AccountId),
            command => command.CommandId == SyncTestCommands.GetId(create)
        );
        Assert.Equal((SyncResultStatus.Rejected, "invalid"), (stored.Status, stored.Reason));
    }

    [Fact]
    public async Task Sync_CommandOlderThanRetention_RejectedTooOld()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var create = device.CreateNote(
            Guid.NewGuid(),
            fixture.Api.TimeProvider.GetUtcNow().AddMilliseconds(-RetentionMilliseconds - 1),
            "note"
        );

        var response = await device.SyncAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "rejected", "tooOld"), Assert.Single(response.Results));
        Assert.Equal(0, await TestNoteQueries.CountInScopeAsync(fixture.Api, device.PersonalScopeId));
    }

    [Fact]
    public async Task Sync_CommandAheadOfServerClock_RejectedClockAhead()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var create = device.CreateNote(
            Guid.NewGuid(),
            fixture.Api.TimeProvider.GetUtcNow().AddMilliseconds(ClockAheadToleranceMilliseconds + 1),
            "note"
        );

        var response = await device.SyncAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "rejected", "clockAhead"), Assert.Single(response.Results));
        Assert.Equal(0, await TestNoteQueries.CountInScopeAsync(fixture.Api, device.PersonalScopeId));
    }

    [Theory]
    [InlineData(-RetentionMilliseconds)]
    [InlineData(ClockAheadToleranceMilliseconds)]
    public async Task Sync_CommandTimeAtToleranceBoundary_Applied(long offsetMilliseconds)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var create = device.CreateNote(Guid.NewGuid(), fixture.Api.TimeProvider.GetUtcNow().AddMilliseconds(offsetMilliseconds), "note");

        var response = await device.SyncAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "applied"), Assert.Single(response.Results));
    }

    [Theory]
    [InlineData(-46 * MillisecondsPerDay)]
    [InlineData(6 * MillisecondsPerMinute)]
    public async Task Sync_InvalidCommandOutsideTimeTolerance_RejectedInvalid(long offsetMilliseconds)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var create = SyncTestCommands.WithField(
            device.CreateNote(Guid.NewGuid(), fixture.Api.TimeProvider.GetUtcNow().AddMilliseconds(offsetMilliseconds), "note"),
            "subtitle",
            "unknown"
        );

        var response = await device.SyncAsync(create);

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), "rejected", "invalid"), Assert.Single(response.Results));
    }

    [Fact]
    public async Task Sync_LateResendOfAppliedCommand_ReturnsAlreadyProcessed()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var create = device.CreateNote(
            Guid.NewGuid(),
            fixture.Api.TimeProvider.GetUtcNow().AddMilliseconds(-RetentionMilliseconds),
            "note"
        );
        Assert.Equal("applied", Assert.Single((await device.SyncAsync(create)).Results).Status);
        fixture.Api.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));

        var response = await device.SyncAsync(create);

        Assert.Equal(
            new SyncCommandResult(SyncTestCommands.GetId(create), "alreadyProcessed", Original: AppliedOriginal),
            Assert.Single(response.Results)
        );
    }

    [Theory]
    [InlineData("laterTimeWins")]
    [InlineData("sameTimeLaterCommandWins")]
    [InlineData("groupsWinSeparately")]
    [InlineData("deleteWins")]
    [InlineData("deletedIdStaysDeleted")]
    [InlineData("createOnExistingIdActsAsSetFields")]
    [InlineData("setFieldsWithoutTarget")]
    [InlineData("deleteWithoutTarget")]
    public async Task Sync_CommandSequence_EndsAsConflictRulesRequire(string sequence)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        var missingNoteId = Guid.NewGuid();
        await device.SyncAsync(device.CreateNote(noteId, now.AddSeconds(-60), "a"));
        (IReadOnlyList<(JsonObject Command, string Status, string? Cause)> Steps, Guid NoteId, string? Fields) expected = sequence switch
        {
            "laterTimeWins" => (
                [(SetTitle(-10, "b"), "applied", null), (SetTitle(-20, "c"), "ignored", "lostToNewer")],
                noteId,
                PlainNoteFields("b")
            ),
            "sameTimeLaterCommandWins" => (
                [(SetTitle(-10, "b"), "applied", null), (SetTitle(-10, "c"), "applied", null)],
                noteId,
                PlainNoteFields("c")
            ),
            "groupsWinSeparately" => (
                [
                    (SetTitle(-10, "b"), "applied", null),
                    (device.SetNoteFields(noteId, now.AddSeconds(-30), new JsonObject { ["color"] = "red", ["weight"] = 1.5m }), "applied", null),
                ],
                noteId,
                """{"title":"b","color":"red","weight":1.5,"pinned":false}"""
            ),
            "deleteWins" => (
                [
                    (SetTitle(-10, "b"), "applied", null),
                    (device.DeleteNote(noteId, now.AddSeconds(-50)), "applied", null),
                    (SetTitle(0, "c"), "ignored", "targetDeleted"),
                ],
                noteId,
                null
            ),
            "deletedIdStaysDeleted" => (
                [(device.DeleteNote(noteId, now.AddSeconds(-10)), "applied", null), (device.CreateNote(noteId, now, "b"), "ignored", "targetDeleted")],
                noteId,
                null
            ),
            "createOnExistingIdActsAsSetFields" => (
                [
                    (device.CreateNote(noteId, now.AddSeconds(-5), "z"), "applied", null),
                    (device.CreateNote(noteId, now.AddSeconds(-30), "y"), "ignored", "lostToNewer"),
                ],
                noteId,
                PlainNoteFields("z")
            ),
            "setFieldsWithoutTarget" => (
                [(device.SetNoteFields(missingNoteId, now, SyncTestCommands.TitleFields("b")), "ignored", "targetDeleted")],
                missingNoteId,
                null
            ),
            "deleteWithoutTarget" => ([(device.DeleteNote(missingNoteId, now), "ignored", "targetDeleted")], missingNoteId, null),
            _ => throw new ArgumentOutOfRangeException(nameof(sequence), sequence, null),
        };

        foreach (var (command, status, cause) in expected.Steps)
        {
            var response = await device.SyncAsync(command);
            Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(command), status, Cause: cause), Assert.Single(response.Results));
        }

        var finalState = Assert.Single((await device.OtherDevice().SyncAsync()).Scopes);
        Assert.Equal(expected.Fields, finalState.Records.SingleOrDefault(record => record.Id == expected.NoteId)?.Fields.GetRawText());

        JsonObject SetTitle(int offsetSeconds, string title) =>
            device.SetNoteFields(noteId, now.AddSeconds(offsetSeconds), SyncTestCommands.TitleFields(title));
    }

    [Theory]
    [InlineData(false, TestNoteType.RejectingTitle, "rejected", "invalid", null)]
    [InlineData(true, TestNoteType.IgnoringTitle, "ignored", null, "lostToNewer")]
    [InlineData(true, TestNoteType.RejectingTitle, "rejected", "invalid", null)]
    public async Task Sync_DataTypeRule_RejectsOrIgnoresCommand(
        bool noteExists,
        string title,
        string expectedStatus,
        string? expectedReason,
        string? expectedCause
    )
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        if (noteExists)
        {
            await device.SyncAsync(device.CreateNote(noteId, now.AddSeconds(-10), "kept"));
        }

        var command = noteExists ? device.SetNoteFields(noteId, now, SyncTestCommands.TitleFields(title)) : device.CreateNote(noteId, now, title);

        var response = await device.SyncAsync(command);

        Assert.Equal(
            new SyncCommandResult(SyncTestCommands.GetId(command), expectedStatus, expectedReason, expectedCause),
            Assert.Single(response.Results)
        );
        Assert.Equal(noteExists ? "kept" : null, await TestNoteQueries.ReadTitleAsync(fixture.Api, noteId));
    }

    [Fact]
    public async Task Sync_FailingCommand_KeepsEarlierCommandsAndReturnsFailed()
    {
        var logRecords = new List<LogRecord>();
        var traceId = ActivityTraceId.CreateRandom();
        SyncTestDevice device;
        JsonObject failingChange;
        await using (var api = await ApiHost.StartAsync(fixture.CreateDatabaseConnectionString(), logRecords: logRecords, testDataTypes: true))
        {
            device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            var noteId = Guid.NewGuid();
            var laterNoteId = Guid.NewGuid();
            var create = device.CreateNote(noteId, now.AddSeconds(-10), "before");
            failingChange = device.SetNoteFields(noteId, now, SyncTestCommands.TitleFields(TestNoteType.FailingTitle));
            var laterCreate = device.CreateNote(laterNoteId, now, "later");

            using var response = await device.SendAsync(
                JsonContent.Create(device.CreateBody(create, failingChange, laterCreate)),
                device.DeviceId.ToString(),
                TestTelemetry.CreateTraceParent(traceId)
            );

            Assert.Equal(
                new SyncCommandResult[] { new(SyncTestCommands.GetId(create), "applied"), new(SyncTestCommands.GetId(failingChange), "failed") },
                (await SyncTestDevice.ReadResponseAsync(response)).Results
            );
            Assert.Equal("before", await TestNoteQueries.ReadTitleAsync(api, noteId));
            Assert.Null(await TestNoteQueries.ReadTitleAsync(api, laterNoteId));
            Guid[] unprocessedCommandIds = [SyncTestCommands.GetId(failingChange), SyncTestCommands.GetId(laterCreate)];
            Assert.DoesNotContain(
                await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId),
                command => unprocessedCommandIds.Contains(command.CommandId)
            );

            var resent = await device.SyncAsync(failingChange, laterCreate);

            Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(failingChange), "failed"), Assert.Single(resent.Results));
        }

        var failure = Assert.Single(
            logRecords,
            logRecord => logRecord.TraceId == traceId && logRecord.EventId.Name == "SyncCommandFailed"
        );
        Assert.Equal(LogLevel.Error, failure.LogLevel);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        var attributes = failure.Attributes!.ToDictionary();
        Assert.Equal(SyncTestCommands.GetId(failingChange), attributes["CommandId"]);
        Assert.Equal(TestNoteType.DataTypeName, attributes["DataType"]);
        Assert.Equal("setFields", attributes["CommandKind"]);
        Assert.Equal(device.DeviceId, attributes["DeviceId"]);
        Assert.Equal(device.AccountId, TestTelemetry.FindUserId(failure));
        Assert.DoesNotContain(TestNoteType.FailingTitle, failure.FormattedMessage);
    }

    [Fact]
    public async Task Sync_LockTimeoutAfterRetries_ReturnsServiceUnavailableWithRetryAfter()
    {
        var logRecords = new List<LogRecord>();
        var traceId = ActivityTraceId.CreateRandom();
        var connectionString = fixture.CreateDatabaseConnectionString();
        await using (
            var api = await ApiHost.StartAsync(
                connectionString,
                new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
                logRecords: logRecords,
                testDataTypes: true
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            var noteId = Guid.NewGuid();
            await device.SyncAsync(device.CreateNote(noteId, now.AddSeconds(-10), "before"));
            var tooOldCreate = device.CreateNote(Guid.NewGuid(), now.AddDays(-46), "too old");
            var change = device.SetNoteFields(noteId, now, SyncTestCommands.TitleFields("after"));

            await using (await new DatabaseAdministration(connectionString).LockScopeRowAsync(device.PersonalScopeId))
            {
                using var response = await device.SendAsync(
                    JsonContent.Create(device.CreateBody(tooOldCreate, change)),
                    device.DeviceId.ToString(),
                    TestTelemetry.CreateTraceParent(traceId)
                );

                await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, TestProblemTypes.ServiceUnavailable);
                Assert.InRange(Assert.NotNull(response.Headers.RetryAfter?.Delta), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20));
            }

            var resent = await device.SyncAsync(tooOldCreate, change);

            Assert.Equal(
                new SyncCommandResult[]
                {
                    new(SyncTestCommands.GetId(tooOldCreate), "alreadyProcessed", Original: new SyncOriginalResult("rejected", "tooOld")),
                    new(SyncTestCommands.GetId(change), "applied"),
                },
                resent.Results
            );
            Assert.Equal("after", await TestNoteQueries.ReadTitleAsync(api, noteId));
        }

        var traceLogRecords = logRecords.Where(logRecord => logRecord.TraceId == traceId).ToList();
        Assert.Equal(2, traceLogRecords.Count(logRecord => logRecord.EventId.Name == CoreEventId.ExecutionStrategyRetrying.Name));
        var unavailable = Assert.Single(traceLogRecords, logRecord => logRecord.EventId.Name == "SyncDatabaseUnavailable");
        Assert.Equal(LogLevel.Warning, unavailable.LogLevel);
        Assert.Equal("commands", unavailable.Attributes!.ToDictionary()["Phase"]);
    }

    [Fact]
    public async Task Sync_SameRequestTwiceInParallel_AppliesOnceAndReportsOriginalToOther()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var versionBefore = ReadPersonalScopeVersion(device.Token!);
        var create = device.CreateNote(Guid.NewGuid(), fixture.Api.TimeProvider.GetUtcNow(), "note");
        var body = device.CreateBody(create).ToJsonString();
        var administration = new DatabaseAdministration(fixture.ConnectionString);
        Task<SyncResponse>[] sends;
        await using (await administration.LockProcessedCommandsTableAsync())
        {
            sends =
            [
                .. new[] { device, device.OtherDevice() }.Select(async sender =>
                {
                    using var response = await sender.SendAsync(
                        new StringContent(body, Encoding.UTF8, "application/json"),
                        sender.DeviceId.ToString()
                    );
                    return await SyncTestDevice.ReadResponseAsync(response);
                }),
            ];
            await administration.WaitForLockWaitersAsync(sends.Length);
        }

        var responses = await Task.WhenAll(sends);

        var results = responses.Select(response => Assert.Single(response.Results)).ToArray();
        Assert.Contains(new SyncCommandResult(SyncTestCommands.GetId(create), "applied"), results);
        Assert.Contains(new SyncCommandResult(SyncTestCommands.GetId(create), "alreadyProcessed", Original: AppliedOriginal), results);
        Assert.Equal(1, await TestNoteQueries.CountInScopeAsync(fixture.Api, device.PersonalScopeId));
        Assert.All(responses, response => Assert.Equal(versionBefore + 1, ReadPersonalScopeVersion(response.Token)));
    }

    [Fact]
    public async Task Sync_ConcurrentCreatesOfSameIdInTwoScopes_AppliesOneAndRejectsOtherNoAccess()
    {
        SyncTestDevice[] devices = [await SyncTestDevice.SignInAsync(fixture.Api), await SyncTestDevice.SignInAsync(fixture.Api)];
        var noteId = Guid.NewGuid();
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var administration = new DatabaseAdministration(fixture.ConnectionString);
        Task<SyncResponse>[] creates;
        await using (await administration.LockNotesTableAsync())
        {
            creates = [.. devices.Select(device => device.SyncAsync(device.CreateNote(noteId, now, "note")))];
            await administration.WaitForLockWaitersAsync(2);
        }

        var responses = await Task.WhenAll(creates);

        var results = responses.Select(response => Assert.Single(response.Results)).ToArray();
        var winner = Assert.Single(Enumerable.Range(0, devices.Length), index => results[index].Status == "applied");
        var loser = 1 - winner;
        Assert.Equal(("rejected", "noAccess"), (results[loser].Status, results[loser].Reason));
        Assert.Equal(
            devices[winner].PersonalScopeId,
            await fixture.Api.QueryDatabaseAsync(context =>
                context
                    .Set<TestNote>()
                    .Where(note => note.Id == noteId)
                    .Select(note => note.ScopeId)
                    .SingleAsync(TestContext.Current.CancellationToken)
            )
        );
        var loserBlock = Assert.Single(responses[loser].Scopes);
        Assert.Empty(loserBlock.Records);
        Assert.Equal(new SyncDeletedRecord(TestNoteType.DataTypeName, noteId), Assert.Single(loserBlock.DeletedRecords));
    }

    [Fact]
    public async Task Sync_AccountDeletedWhileCommandWaits_ReturnsAccountGone()
    {
        var logRecords = new List<LogRecord>();
        var traceId = ActivityTraceId.CreateRandom();
        var connectionString = fixture.CreateDatabaseConnectionString();
        await using (var api = await ApiHost.StartAsync(connectionString, logRecords: logRecords, testDataTypes: true))
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var create = device.CreateNote(Guid.NewGuid(), api.TimeProvider.GetUtcNow(), "note");
            var administration = new DatabaseAdministration(connectionString);

            await using var deletion = await administration.BeginUserDeletionAsync(device.AccountId);
            var sync = device.SendAsync(
                JsonContent.Create(device.CreateBody(create)),
                device.DeviceId.ToString(),
                TestTelemetry.CreateTraceParent(traceId)
            );
            await administration.WaitForLockWaitersAsync(1);
            await deletion.CommitAsync();
            using var response = await sync;

            await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.Gone, TestProblemTypes.AccountGone);
        }

        Assert.DoesNotContain(
            logRecords,
            logRecord =>
                logRecord.TraceId == traceId
                && (logRecord.EventId.Name == "SyncCommandFailed" || logRecord.EventId.Name == "SyncDatabaseUnavailable")
        );
    }

    [Theory]
    [InlineData("timeMissing")]
    [InlineData("idMalformed")]
    public async Task Sync_UnparseableCommand_RejectedInvalidOnEveryResendWithoutClaim(string malformation)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var create = device.CreateNote(Guid.NewGuid(), fixture.Api.TimeProvider.GetUtcNow(), "note");
        (JsonObject Command, Guid? CommandId) expected = malformation switch
        {
            "timeMissing" => (SyncTestCommands.Without(create, "time"), SyncTestCommands.GetId(create)),
            "idMalformed" => (SyncTestCommands.With(create, "id", "not-a-guid"), null),
            _ => throw new ArgumentOutOfRangeException(nameof(malformation), malformation, null),
        };

        var first = await device.SyncAsync(expected.Command);
        var second = await device.SyncAsync(expected.Command);

        var expectedResult = new SyncCommandResult(expected.CommandId, "rejected", "invalid");
        Assert.Equal(expectedResult, Assert.Single(first.Results));
        Assert.Equal(expectedResult, Assert.Single(second.Results));
        Assert.Empty(await TestNoteQueries.ReadProcessedCommandsAsync(fixture.Api, device.AccountId));
    }

    private static string CreateInvalidCommand(SyncTestDevice device, DateTimeOffset now, string invalidity)
    {
        var noteId = Guid.NewGuid();
        return invalidity switch
        {
            "titleTooLong" => SyncTestCommands.WithField(Create(), "title", new string('x', 101)).ToJsonString(),
            "titleTooLongInUtf16Units" => SyncTestCommands
                .WithField(Create(), "title", string.Concat(Enumerable.Repeat("\U0001F600", 51)))
                .ToJsonString(),
            "unknownField" => SyncTestCommands.WithField(Create(), "subtitle", "unknown").ToJsonString(),
            "partialGroup" => SyncTestCommands.WithField(Create(), "color", "red").ToJsonString(),
            "loneSurrogateInTitle" => Create().ToJsonString().Replace($"\"{PlaceholderTitle}\"", LoneSurrogateJson, StringComparison.Ordinal),
            "nullCharacterInTitle" => SyncTestCommands.WithField(Create(), "title", "a\0b").ToJsonString(),
            "timeInMicroseconds" => SyncTestCommands.With(Create(), "time", 1_795_400_000_123_000).ToJsonString(),
            "serverField" => SyncTestCommands.WithField(Create(), "pinned", true).ToJsonString(),
            "unknownKey" => SyncTestCommands.WithField(CreateStyled(), "color", "purple").ToJsonString(),
            "weightAtExclusiveMinimum" => SyncTestCommands.WithField(CreateStyled(), "weight", 0).ToJsonString(),
            "weightAboveMaximum" => SyncTestCommands.WithField(CreateStyled(), "weight", 100.5m).ToJsonString(),
            "weightWithTooManyDecimals" => SyncTestCommands.WithField(CreateStyled(), "weight", 1.25m).ToJsonString(),
            "fieldsOnDelete" => SyncTestCommands
                .With(device.DeleteNote(noteId, now), "fields", SyncTestCommands.TitleFields(PlaceholderTitle))
                .ToJsonString(),
            "emptyFieldsOnSetFields" => device.SetNoteFields(noteId, now, new JsonObject()).ToJsonString(),
            "createWithoutTitle" => SyncTestCommands
                .With(CreateStyled(), "fields", new JsonObject { ["color"] = "red", ["weight"] = 1 })
                .ToJsonString(),
            "unknownCommandMember" => SyncTestCommands.With(Create(), "extra", true).ToJsonString(),
            "unknownKind" => SyncTestCommands.With(Create(), "kind", "move").ToJsonString(),
            "unregisteredDataType" => SyncTestCommands.With(Create(), "dataType", "unknownType").ToJsonString(),
            "recordIdEqualsScopeId" => SyncTestCommands.With(Create(), "recordId", device.PersonalScopeId).ToJsonString(),
            "duplicateTitle" => Create()
                .ToJsonString()
                .Replace(
                    $"\"title\":\"{PlaceholderTitle}\"",
                    $"\"title\":\"{PlaceholderTitle}\",\"title\":\"duplicate\"",
                    StringComparison.Ordinal
                ),
            "titleAsNumber" => SyncTestCommands.WithField(Create(), "title", 5).ToJsonString(),
            "nullCharacterInKind" => SyncTestCommands.With(Create(), "kind", "create\0").ToJsonString(),
            "loneSurrogateInDataType" => Create()
                .ToJsonString()
                .Replace($"\"{TestNoteType.DataTypeName}\"", LoneSurrogateJson, StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidity), invalidity, null),
        };

        JsonObject Create() => device.CreateNote(noteId, now, PlaceholderTitle);

        JsonObject CreateStyled() => device.CreateNote(noteId, now, PlaceholderTitle, "red", 1m);
    }

    private static string PlainNoteFields(string title) => $$"""{"title":"{{title}}","color":null,"weight":null,"pinned":false}""";

    private static long ReadPersonalScopeVersion(string token) => Assert.Single(SyncTokenFormat.Decode(token).Scopes).Version;

    private Task<List<Guid>> ReadNoteIdsAsync(Guid scopeId) =>
        fixture.Api.QueryDatabaseAsync(context =>
            context
                .Set<TestNote>()
                .Where(note => note.ScopeId == scopeId)
                .Select(note => note.Id)
                .ToListAsync(TestContext.Current.CancellationToken)
        );
}
