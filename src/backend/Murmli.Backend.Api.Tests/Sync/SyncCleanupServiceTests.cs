using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.Protocol;
using Npgsql;
using OpenTelemetry.Logs;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncCleanupServiceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string CompletedEvent = "SyncCleanupCompleted";

    private const string SkippedEvent = "SyncCleanupSkipped";

    private const string FailedEvent = "SyncCleanupFailed";

    private const string StoppedEvent = "SyncCleanupStopped";

    private static readonly TimeSpan Retention = TimeSpan.FromDays(45);

    private static readonly TimeSpan SafetyMargin = TimeSpan.FromDays(1);

    private static readonly TimeSpan ClockAheadTolerance = TimeSpan.FromMinutes(5);

    public static TheoryData<int> CancelledStatements => [.. Enumerable.Range(1, 16)];

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task RunAsync_ExpiredAndYoungerEntries_RemovesOnlyExpired(int batchSize)
    {
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(
            postgres.CreateDatabaseConnectionString(),
            new Dictionary<string, string?> { ["Sync:Cleanup:BatchSize"] = $"{batchSize}" },
            logRecords: logRecords,
            testDataTypes: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var start = api.TimeProvider.GetUtcNow();
        await CreateAndDeleteNoteAsync(api, device);
        api.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var withinMargin = await CreateAndDeleteNoteAsync(api, device);
        api.TimeProvider.SetUtcNow(start + Retention + SafetyMargin + TimeSpan.FromMinutes(1));
        var young = await CreateAndDeleteNoteAsync(api, device);

        await RunCleanupAsync(api);

        Assert.Equal([young.NoteId], (await ReadTombstonesAsync(api)).Select(tombstone => tombstone.RecordId));
        Assert.Equal(
            new[] { withinMargin.CreateId, withinMargin.DeleteId, young.CreateId, young.DeleteId }.Order(),
            (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId)).Select(command => command.CommandId).Order()
        );
        Assert.Equal(4, (await SharedScopeQueries.ReadScopeAsync(api, device.PersonalScopeId))!.TombstoneFloor);
        await AssertLeaseReleasedAsync(api);
        var completed = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == CompletedEvent);
        Assert.Equal(LogLevel.Information, completed.LogLevel);
        var attributes = completed.Attributes!.ToDictionary();
        Assert.Equal(2, attributes["RemovedTombstoneCount"]);
        Assert.Equal(2, attributes["RemovedProcessedCommandCount"]);
        Assert.Equal(0, attributes["RemovedRecordCount"]);
        Assert.IsType<long>(attributes["DurationMilliseconds"]);
        Assert.DoesNotContain(logRecords, logRecord => logRecord.EventId.Name is SkippedEvent or FailedEvent);
    }

    [Fact]
    public async Task RunAsync_YoungerTombstoneBelowFloorAfterClockStep_KeepsIt()
    {
        await using var api = await ApiHost.StartAsync(postgres.CreateDatabaseConnectionString(), testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        var start = api.TimeProvider.GetUtcNow();
        var youngerNoteId = await DeleteNotesAcrossClockStepBackAsync(api, device, start);
        api.TimeProvider.SetUtcNow(start + Retention + TimeSpan.FromMinutes(1));

        await RunCleanupAsync(api);

        Assert.Equal([youngerNoteId], (await ReadTombstonesAsync(api)).Select(tombstone => tombstone.RecordId));
        Assert.Equal(4, (await SharedScopeQueries.ReadScopeAsync(api, device.PersonalScopeId))!.TombstoneFloor);
    }

    [Fact]
    public async Task RunAsync_TombstoneBelowFloorExpiresInLaterRun_KeepsFloor()
    {
        await using var api = await ApiHost.StartAsync(postgres.CreateDatabaseConnectionString(), testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        var start = api.TimeProvider.GetUtcNow();
        await DeleteNotesAcrossClockStepBackAsync(api, device, start);
        api.TimeProvider.SetUtcNow(start + Retention + TimeSpan.FromMinutes(1));
        await RunCleanupAsync(api);
        api.TimeProvider.Advance(Retention);

        await RunCleanupAsync(api);

        Assert.Empty(await ReadTombstonesAsync(api));
        Assert.Equal(4, (await SharedScopeQueries.ReadScopeAsync(api, device.PersonalScopeId))!.TombstoneFloor);
    }

    [Fact]
    public async Task RunAsync_CommandTimeBeforeProcessing_RemovesIdByCommandTime()
    {
        await using var api = await ApiHost.StartAsync(postgres.CreateDatabaseConnectionString(), testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        var start = api.TimeProvider.GetUtcNow();
        var offlineTime = start - TimeSpan.FromDays(10);
        var offlineCreate = device.CreateNote(Guid.NewGuid(), offlineTime, "note");
        var currentCreate = device.CreateNote(Guid.NewGuid(), start, "note");
        Assert.All((await device.SyncAsync(offlineCreate, currentCreate)).Results, result => Assert.Equal("applied", result.Status));
        api.TimeProvider.SetUtcNow(offlineTime + Retention + SafetyMargin + TimeSpan.FromMinutes(1));

        await RunCleanupAsync(api);

        Assert.Equal(
            [SyncTestCommands.GetId(currentCreate)],
            (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId)).Select(command => command.CommandId)
        );
        Assert.Equal(
            new SyncCommandResult(SyncTestCommands.GetId(offlineCreate), "rejected", "tooOld"),
            Assert.Single((await device.SyncAsync(offlineCreate)).Results)
        );
    }

    [Theory]
    [InlineData("withinRetention", "alreadyProcessed", false)]
    [InlineData("aheadOfClockWithinSafetyMargin", "alreadyProcessed", true)]
    [InlineData("afterRetentionAndSafetyMargin", "rejected", true)]
    public async Task RunAsync_ResendAfterCleanup_NeverAppliedTwice(string scenario, string expectedStatus, bool tombstoneRemoved)
    {
        await using var api = await ApiHost.StartAsync(postgres.CreateDatabaseConnectionString(), testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        var start = api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        var create = device.CreateNote(noteId, start + ClockAheadTolerance - TimeSpan.FromMinutes(1), "note");
        Assert.Equal("applied", Assert.Single((await device.SyncAsync(create)).Results).Status);
        await device.ApplyOnOtherDeviceAsync(device.DeleteNote(noteId, start));
        api.TimeProvider.SetUtcNow(
            scenario switch
            {
                "withinRetention" => start + Retention - TimeSpan.FromMinutes(1),
                "aheadOfClockWithinSafetyMargin" => start + Retention + TimeSpan.FromMinutes(2),
                "afterRetentionAndSafetyMargin" => start + Retention + SafetyMargin + ClockAheadTolerance,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
            }
        );

        await RunCleanupAsync(api);

        Assert.Equal(tombstoneRemoved, (await ReadTombstonesAsync(api)).Count == 0);
        Assert.Equal(
            expectedStatus == "alreadyProcessed",
            (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId)).Any(command =>
                command.CommandId == SyncTestCommands.GetId(create)
            )
        );
        var resent = Assert.Single((await device.SyncAsync(create)).Results);
        Assert.Equal(
            expectedStatus == "alreadyProcessed"
                ? new SyncCommandResult(SyncTestCommands.GetId(create), "alreadyProcessed", Original: new SyncOriginalResult("applied"))
                : new SyncCommandResult(SyncTestCommands.GetId(create), "rejected", "tooOld"),
            resent
        );
        Assert.Null(await TestNoteQueries.ReadTitleAsync(api, noteId));
    }

    [Theory]
    [InlineData("originalApplied", "rejected", "tooOld")]
    [InlineData("originalRejectedClockAhead", "applied", null)]
    public async Task RunAsync_IdRemovedBetweenClaimAndReadOfResend_ResendProcessedAsNew(
        string scenario,
        string expectedStatus,
        string? expectedReason
    )
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(connectionString, logRecords: logRecords, testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        var start = api.TimeProvider.GetUtcNow();
        var resendTime = start + Retention + SafetyMargin + TimeSpan.FromMinutes(1);
        var create = device.CreateNote(
            Guid.NewGuid(),
            scenario switch
            {
                "originalApplied" => start,
                "originalRejectedClockAhead" => resendTime + ClockAheadTolerance - TimeSpan.FromMinutes(1),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
            },
            "note"
        );
        await device.SyncAsync(create);
        api.TimeProvider.SetUtcNow(resendTime);
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SyncResponse> resend;
        using (
            ListenToStatements(
                connectionString,
                activity =>
                {
                    // ON CONFLICT DO NOTHING locks no row, so no held lock can put the removal between claim and read
                    if (
                        activity.GetTagItem("db.query.text") is string queryText
                        && queryText.StartsWith("INSERT INTO sync.processed_commands", StringComparison.Ordinal)
                        && claimed.TrySetResult()
                    )
                    {
                        released.Task.Wait(TestContext.Current.CancellationToken);
                    }
                }
            )
        )
        {
            resend = device.SyncAsync(create);
            try
            {
                Assert.Same(claimed.Task, await Task.WhenAny(claimed.Task, resend));
                await RunCleanupAsync(api);
            }
            finally
            {
                released.SetResult();
            }
        }

        Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(create), expectedStatus, expectedReason), Assert.Single((await resend).Results));
        Assert.Equal(
            1,
            Assert.Single(logRecords, logRecord => logRecord.EventId.Name == CompletedEvent).Attributes!.ToDictionary()["RemovedProcessedCommandCount"]
        );
    }

    [Fact]
    public async Task RunAsync_ExpiredRootDelete_StillReportedRemovedByOwnAccount()
    {
        await using var api = await ApiHost.StartAsync(postgres.CreateDatabaseConnectionString(), testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        await device.ApplyAllAsync(device.CreateBoard(boardId, now, "board"));
        var otherDevice = device.OtherDevice();
        await otherDevice.SyncAllAsync();
        var delete = device.DeleteBoard(boardId, now);
        await device.ApplyAllAsync(delete);
        api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));

        await RunCleanupAsync(api);

        Assert.Equal(new SyncRemovedScope(boardId, true), Assert.Single((await otherDevice.SyncAllAsync()).RemovedScopes));
        Assert.Equal(
            [SyncTestCommands.GetId(delete)],
            (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId)).Select(command => command.CommandId)
        );
    }

    [Theory]
    [MemberData(nameof(CancelledStatements))]
    public async Task RunAsync_CancelledAfterAnyStatement_NoResponseMissesDeletion(int cancelledStatement)
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(connectionString, logRecords: logRecords, testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        Guid[] noteIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var boardId = Guid.NewGuid();
        Guid[] cardIds = [Guid.NewGuid(), Guid.NewGuid()];
        await device.ApplyAllAsync(
            [
                .. noteIds.Select(noteId => device.CreateNote(noteId, now, "note")),
                device.CreateBoard(boardId, now, "board"),
                .. cardIds.Select(cardId => device.CreateCard(boardId, cardId, now, "card")),
            ]
        );
        var reader = device.OtherDevice();
        await reader.SyncAllAsync();
        await device.ApplyAllAsync(device.DeleteNote(noteIds[0], now), device.DeleteNote(noteIds[1], now), device.DeleteCard(boardId, cardIds[0], now));
        api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var statementCount = 0;
        using (
            ListenToStatements(
                connectionString,
                _ =>
                {
                    if (Interlocked.Increment(ref statementCount) == cancelledStatement)
                    {
                        cancellation.Cancel();
                    }
                }
            )
        )
        {
            try
            {
                await api.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }

        Assert.True(cancellation.IsCancellationRequested, $"The run issued fewer than {cancelledStatement} statements.");

        var response = await reader.SyncAllAsync();

        var personalBlock = Assert.Single(response.Scopes, block => block.ScopeId == device.PersonalScopeId);
        Assert.Equal([noteIds[2]], ApplyBlock(noteIds, personalBlock));
        var boardBlock = Assert.Single(response.Scopes, block => block.ScopeId == boardId);
        Assert.Equal(new[] { boardId, cardIds[1] }.Order(), ApplyBlock([boardId, .. cardIds], boardBlock).Order());
        await RunCleanupAsync(api);
        Assert.Empty(await ReadTombstonesAsync(api));
        Assert.Contains(logRecords, logRecord => logRecord.EventId.Name == CompletedEvent);
    }

    [Fact]
    public async Task RunAsync_TwoConcurrentRuns_DoNotInterfere()
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var firstLogRecords = new List<LogRecord>();
        var secondLogRecords = new List<LogRecord>();
        await using var firstApi = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:30" },
            logRecords: firstLogRecords,
            testDataTypes: true
        );
        await using var secondApi = await ApiHost.StartAsync(
            connectionString,
            logRecords: secondLogRecords,
            testDataTypes: true,
            timeProvider: firstApi.TimeProvider
        );
        var device = await SyncTestDevice.SignInAsync(firstApi);
        var deleted = await CreateAndDeleteNoteAsync(firstApi, device);
        firstApi.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));
        var administration = new DatabaseAdministration(connectionString);
        Task firstRun;
        await using (await administration.LockScopeRowAsync(device.PersonalScopeId))
        {
            firstRun = firstApi.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(TestContext.Current.CancellationToken);
            await administration.WaitForLockWaitersAsync(1);
            firstApi.TimeProvider.Advance(TimeSpan.FromMinutes(1));

            await RunCleanupAsync(secondApi);

            Assert.False(firstRun.IsCompleted);
            Assert.Equal([deleted.NoteId], (await ReadTombstonesAsync(firstApi)).Select(tombstone => tombstone.RecordId));
        }

        await firstRun;

        Assert.Empty(await ReadTombstonesAsync(firstApi));
        Assert.Equal(2, (await SharedScopeQueries.ReadScopeAsync(firstApi, device.PersonalScopeId))!.TombstoneFloor);
        await AssertLeaseReleasedAsync(firstApi);
        Assert.Equal([SkippedEvent], secondLogRecords.Select(logRecord => logRecord.EventId.Name).Where(IsCleanupEvent));
        Assert.Equal([CompletedEvent], firstLogRecords.Select(logRecord => logRecord.EventId.Name).Where(IsCleanupEvent));
        Assert.Equal(1, Assert.Single(firstLogRecords, logRecord => logRecord.EventId.Name == CompletedEvent).Attributes!.ToDictionary()["RemovedTombstoneCount"]);
    }

    [Fact]
    public async Task RunAsync_LeaseOfOtherRun_SkipsUntilLeaseExpires()
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(connectionString, logRecords: logRecords, testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        await CreateAndDeleteNoteAsync(api, device);
        api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));
        await new DatabaseAdministration(connectionString).HoldCleanupLeaseAsync(api.TimeProvider.GetUtcNow() + TimeSpan.FromMinutes(1));

        await RunCleanupAsync(api);
        api.TimeProvider.Advance(TimeSpan.FromSeconds(30));
        await RunCleanupAsync(api);
        var tombstonesWhileHeld = await ReadTombstonesAsync(api);
        api.TimeProvider.Advance(TimeSpan.FromSeconds(30) + TimeSpan.FromMilliseconds(1));
        await RunCleanupAsync(api);

        Assert.Single(tombstonesWhileHeld);
        Assert.Empty(await ReadTombstonesAsync(api));
        Assert.Equal([SkippedEvent, SkippedEvent, CompletedEvent], logRecords.Select(logRecord => logRecord.EventId.Name).Where(IsCleanupEvent));
        await AssertLeaseReleasedAsync(api);
    }

    [Fact]
    public async Task RunAsync_ContributorThrows_RunsLaterContributorsAndCompletes()
    {
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(
            postgres.CreateDatabaseConnectionString(),
            logRecords: logRecords,
            testDataTypes: true,
            testCleanupContributors: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        await CreateAndDeleteNoteAsync(api, device);
        api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));

        await RunCleanupAsync(api);

        Assert.Empty(await ReadTombstonesAsync(api));
        var failed = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == FailedEvent);
        Assert.Equal(LogLevel.Error, failed.LogLevel);
        Assert.Equal(ThrowingCleanupContributor.FailureMessage, Assert.IsType<InvalidOperationException>(failed.Exception).Message);
        var attributes = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == CompletedEvent).Attributes!.ToDictionary();
        Assert.Equal(1, attributes["RemovedTombstoneCount"]);
        Assert.Equal(ConstantCleanupContributor.RemovedRecordCount, attributes["RemovedRecordCount"]);
    }

    [Fact]
    public async Task RunAsync_ContributorRegistered_ReceivesServerTimeInMillisecondsAndBatchSize()
    {
        var cleanupContexts = new List<SyncCleanupContext>();
        await using var api = await ApiHost.StartAsync(
            postgres.CreateDatabaseConnectionString(),
            new Dictionary<string, string?> { ["Sync:Cleanup:BatchSize"] = "7" },
            cleanupContexts: cleanupContexts
        );
        var start = api.TimeProvider.GetUtcNow();
        api.TimeProvider.Advance(TimeSpan.FromMicroseconds(1_500));

        await RunCleanupAsync(api);

        Assert.Equal(new SyncCleanupContext(start + TimeSpan.FromMilliseconds(1), 7), Assert.Single(cleanupContexts));
    }

    [Fact]
    public async Task RunAsync_ScopeLockedPastLockTimeout_SkipsOnlyThatScope()
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
            logRecords: logRecords,
            testDataTypes: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await CreateAndDeleteNoteAsync(api, device);
        await device.ApplyAllAsync(device.CreateBoard(boardId, now, "board"), device.CreateCard(boardId, cardId, now, "card"), device.DeleteCard(boardId, cardId, now));
        api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));

        await using (await new DatabaseAdministration(connectionString).LockScopeRowAsync(boardId))
        {
            await RunCleanupAsync(api);
        }

        var tombstonesAfterLockedRun = await ReadTombstonesAsync(api);
        var boardFloorAfterLockedRun = (await SharedScopeQueries.ReadScopeAsync(api, boardId))!.TombstoneFloor;
        await RunCleanupAsync(api);

        Assert.Equal([cardId], tombstonesAfterLockedRun.Select(tombstone => tombstone.RecordId));
        Assert.Equal(0, boardFloorAfterLockedRun);
        Assert.Empty(await ReadTombstonesAsync(api));
        Assert.Equal([CompletedEvent, CompletedEvent], logRecords.Select(logRecord => logRecord.EventId.Name).Where(IsCleanupEvent));
        Assert.All(
            logRecords.Where(logRecord => logRecord.EventId.Name == CompletedEvent),
            completed => Assert.Equal(1, completed.Attributes!.ToDictionary()["RemovedTombstoneCount"])
        );
    }

    [Fact]
    public async Task RunAsync_ProcessedCommandsLockedPastLockTimeout_LogsFailedAndReleasesLease()
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
            logRecords: logRecords,
            testDataTypes: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        await CreateAndDeleteNoteAsync(api, device);
        api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));

        await using (await new DatabaseAdministration(connectionString).LockProcessedCommandsTableAsync())
        {
            await RunCleanupAsync(api);
        }

        Assert.Empty(await ReadTombstonesAsync(api));
        Assert.Equal(2, (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId)).Count);
        await AssertLeaseReleasedAsync(api);
        Assert.Equal([FailedEvent], logRecords.Select(logRecord => logRecord.EventId.Name).Where(IsCleanupEvent));
        Assert.IsType<RetryLimitExceededException>(Assert.Single(logRecords, logRecord => logRecord.EventId.Name == FailedEvent).Exception);
    }

    [Fact]
    public async Task RunAsync_ContributorReachesTimeLimit_RemovesExpiredEntriesAndLogsStopped()
    {
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(
            postgres.CreateDatabaseConnectionString(),
            // The smallest lease; the run reaches its time limit after one minute of real time
            new Dictionary<string, string?> { ["Sync:Cleanup:LeaseDuration"] = "00:02:00" },
            logRecords: logRecords,
            testDataTypes: true,
            waitingCleanupContributor: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        await CreateAndDeleteNoteAsync(api, device);
        api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));

        await RunCleanupAsync(api);

        Assert.Empty(await ReadTombstonesAsync(api));
        await AssertLeaseReleasedAsync(api);
        Assert.Equal([StoppedEvent], logRecords.Select(logRecord => logRecord.EventId.Name).Where(IsCleanupEvent));
        var stopped = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == StoppedEvent);
        Assert.Equal(LogLevel.Warning, stopped.LogLevel);
        Assert.Equal(1, stopped.Attributes!.ToDictionary()["RemovedTombstoneCount"]);
    }

    [Fact]
    public async Task ExecuteAsync_InitialDelayAndIntervalElapse_RunsCleanupOnSchedule()
    {
        await using var api = await ApiHost.StartAsync(
            postgres.CreateDatabaseConnectionString(),
            new Dictionary<string, string?> { ["Sync:Cleanup:Enabled"] = "true", ["Sync:Retention"] = "00:01:00" },
            testDataTypes: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        await CreateAndDeleteNoteAsync(api, device);

        await AdvanceUntilTombstonesRemovedAsync(api, TimeSpan.FromMinutes(2));
        var laterDeleted = await CreateAndDeleteNoteAsync(api, device);
        api.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        var tombstonesBeforeInterval = await ReadTombstonesAsync(api);
        await AdvanceUntilTombstonesRemovedAsync(api, TimeSpan.FromDays(1));

        Assert.Equal([laterDeleted.NoteId], tombstonesBeforeInterval.Select(tombstone => tombstone.RecordId));
    }

    private static bool IsCleanupEvent(string? eventName) => eventName is CompletedEvent or SkippedEvent or FailedEvent or StoppedEvent;

    private static Guid[] ApplyBlock(IEnumerable<Guid> localIds, SyncScopeBlock block) =>
        block.FullState
            ? [.. block.Records.Select(record => record.Id)]
            : [.. localIds.Union(block.Records.Select(record => record.Id)).Except(block.DeletedRecords.Select(deletion => deletion.Id))];

    private static ActivityListener ListenToStatements(string connectionString, Action<Activity> statementStopped)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("db.namespace") as string == database && !activity.DisplayName.StartsWith("CONNECT ", StringComparison.Ordinal))
                {
                    statementStopped(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static Task RunCleanupAsync(ApiHost api) =>
        api.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(TestContext.Current.CancellationToken);

    private static async Task<(Guid NoteId, Guid CreateId, Guid DeleteId)> CreateAndDeleteNoteAsync(ApiHost api, SyncTestDevice device)
    {
        var now = api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        var create = device.CreateNote(noteId, now, "note");
        var delete = device.DeleteNote(noteId, now);
        Assert.All((await device.SyncAsync(create, delete)).Results, result => Assert.Equal("applied", result.Status));
        return (noteId, SyncTestCommands.GetId(create), SyncTestCommands.GetId(delete));
    }

    private static async Task<Guid> DeleteNotesAcrossClockStepBackAsync(ApiHost api, SyncTestDevice device, DateTimeOffset start)
    {
        api.TimeProvider.SetUtcNow(start + Retention);
        var younger = await CreateAndDeleteNoteAsync(api, device);
        api.TimeProvider.AdjustTime(start);
        await CreateAndDeleteNoteAsync(api, device);
        return younger.NoteId;
    }

    private static Task<List<SyncTombstone>> ReadTombstonesAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.Set<SyncTombstone>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));

    private static async Task AssertLeaseReleasedAsync(ApiHost api)
    {
        var lease = await api.QueryDatabaseAsync(context =>
            context.Set<CleanupLease>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)
        );
        Assert.Null(lease.Holder);
        Assert.Null(lease.ExpiresAt);
    }

    // The service registers its next timer only after the previous run, so the clock moves on until the run has happened
    private static async Task AdvanceUntilTombstonesRemovedAsync(ApiHost api, TimeSpan step)
    {
        var start = Stopwatch.GetTimestamp();
        while ((await ReadTombstonesAsync(api)).Count > 0)
        {
            Assert.True(Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(10), "The scheduled cleanup run did not remove the tombstones within 10 seconds.");
            api.TimeProvider.Advance(step);
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }
    }
}
