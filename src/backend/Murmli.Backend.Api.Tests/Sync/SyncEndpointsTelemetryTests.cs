using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Protocol;
using Murmli.Backend.Sync.Writing;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace Murmli.Backend.Api.Tests.Sync;

[Collection<TelemetryCollection>]
public class SyncEndpointsTelemetryTests(PostgresFixture postgres)
{
    private const string DatabaseCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    private static readonly string[] CommandTags =
    [
        "murmli.sync.command.status",
        "murmli.sync.command.reason",
        "murmli.sync.command.kind",
        "murmli.sync.data_type",
        "murmli.sync.writer",
    ];

    [Fact]
    public async Task Sync_Operations_EmitDocumentedMetricsAndLogs()
    {
        var activities = new List<Activity>();
        var logRecords = new List<LogRecord>();
        var metricSnapshots = new List<MetricSnapshot>();
        var traceId = ActivityTraceId.CreateRandom();
        var boardId = Guid.NewGuid();
        SyncTestDevice device;
        JsonObject applied;
        JsonObject rejected;
        long responseLength;
        await using (
            var api = await ApiHost.StartAsync(
                postgres.CreateDatabaseConnectionString(),
                activities: activities,
                logRecords: logRecords,
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            applied = device.CreateNote(Guid.NewGuid(), now, "note");
            rejected = device.CreateNote(Guid.NewGuid(), now, TestNoteType.RejectingTitle);
            var ignored = device.SetNoteFields(Guid.NewGuid(), now, SyncTestCommands.TitleFields("missing"));
            var failing = device.CreateNote(Guid.NewGuid(), now, TestNoteType.FailingTitle);
            var unprocessed = device.CreateNote(Guid.NewGuid(), now, "after failure");
            var body = device.CreateBody(applied, rejected, ignored, failing, unprocessed);
            body["pendingCommandCount"] = 7;
            body["oldestPendingCommandAgeSeconds"] = 42;
            using (
                var response = await device.SendAsync(
                    JsonContent.Create(body),
                    device.DeviceId.ToString(),
                    TestTelemetry.CreateTraceParent(traceId)
                )
            )
            {
                responseLength = Assert.NotNull(response.Content.Headers.ContentLength);
                var result = await SyncTestDevice.ReadResponseAsync(response);
                Assert.Equal(new[] { "applied", "rejected", "ignored", "failed" }, result.Results.Select(commandResult => commandResult.Status));
                device.Token = result.Token;
            }

            Assert.Equal("alreadyProcessed", Assert.Single((await device.SyncAsync(rejected)).Results).Status);
            var removal = await device.SyncAllAsync(device.CreateBoard(boardId, now, "board"), device.DeleteBoard(boardId, now));
            Assert.Equal(new[] { new SyncRemovedScope(boardId, true) }, removal.RemovedScopes);
            using var invalidRequest = await device.SendAsync(JsonContent.Create(device.CreateBody()), "not-a-device-id");
            Assert.Equal(HttpStatusCode.BadRequest, invalidRequest.StatusCode);
            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long>
            {
                ["applied|none|create|testNote|device"] = 1,
                ["rejected|invalid|create|testNote|device"] = 1,
                ["ignored|targetDeleted|setFields|testNote|device"] = 1,
                ["failed|none|create|testNote|device"] = 1,
                ["alreadyProcessed|none|create|testNote|device"] = 1,
                ["applied|none|create|testBoard|device"] = 1,
                ["applied|none|delete|testBoard|device"] = 1,
            },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.commands", CommandTags)
        );
        Assert.Equal(new Dictionary<string, long> { [""] = 4 }, TestMetrics.CountByTags(metricSnapshots, "murmli.sync.request.commands"));
        Assert.Equal(new Dictionary<string, double> { [""] = 8 }, TestMetrics.HistogramSumByTags(metricSnapshots, "murmli.sync.request.commands"));
        Assert.Equal(new Dictionary<string, long> { [""] = 1 }, TestMetrics.CountByTags(metricSnapshots, "murmli.sync.device.pending_commands"));
        Assert.Equal(
            new Dictionary<string, double> { [""] = 7 },
            TestMetrics.HistogramSumByTags(metricSnapshots, "murmli.sync.device.pending_commands")
        );
        Assert.Equal(new Dictionary<string, long> { [""] = 1 }, TestMetrics.CountByTags(metricSnapshots, "murmli.sync.device.oldest_pending_age"));
        Assert.Equal(
            new Dictionary<string, double> { [""] = 42 },
            TestMetrics.HistogramSumByTags(metricSnapshots, "murmli.sync.device.oldest_pending_age")
        );
        Assert.Equal(
            new Dictionary<string, long> { ["no_token"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.full_state.scopes", "murmli.sync.full_state.reason")
        );
        Assert.Equal(
            new Dictionary<string, long> { ["True"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.scopes.removed", "murmli.sync.removed_by_own_account")
        );
        Assert.Equal(
            new Dictionary<string, long> { ["False"] = 4 },
            TestMetrics.CountByTags(metricSnapshots, "murmli.sync.response.size", "murmli.sync.response.compressed")
        );
        Assert.Empty(TestMetrics.CountByTags(metricSnapshots, "murmli.sync.response.compressed_size"));
        Assert.Equal(
            new Dictionary<string, long> { ["invalid_request"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.requests.rejected", "murmli.sync.rejection.reason")
        );

        var bootstrapped = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == "SyncAccountBootstrapped");
        Assert.Equal(3004, bootstrapped.EventId.Id);
        Assert.Equal(LogLevel.Information, bootstrapped.LogLevel);
        Assert.Equal(device.PersonalScopeId, bootstrapped.Attributes!.ToDictionary()["ScopeId"]);
        Assert.Equal(device.AccountId, TestTelemetry.FindUserId(bootstrapped));

        var commandRejected = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == "SyncCommandRejected");
        Assert.Equal(3000, commandRejected.EventId.Id);
        Assert.Equal(LogLevel.Information, commandRejected.LogLevel);
        var rejectedAttributes = commandRejected.Attributes!.ToDictionary();
        Assert.Equal(device.DeviceId, rejectedAttributes["DeviceId"]);
        Assert.Equal(SyncTestCommands.GetId(rejected), rejectedAttributes["CommandId"]);
        Assert.Equal("testNote", rejectedAttributes["DataType"]);
        Assert.Equal("create", rejectedAttributes["CommandKind"]);
        Assert.Equal(device.PersonalScopeId, rejectedAttributes["ScopeId"]);
        Assert.Equal(rejected["recordId"]!.GetValue<Guid>(), rejectedAttributes["RecordId"]);
        Assert.Equal("invalid", rejectedAttributes["Reason"]);
        Assert.Equal(device.AccountId, TestTelemetry.FindUserId(commandRejected));

        var scopeRemoved = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == "SyncScopeRemoved");
        Assert.Equal(3001, scopeRemoved.EventId.Id);
        Assert.Equal(LogLevel.Information, scopeRemoved.LogLevel);
        var removedAttributes = scopeRemoved.Attributes!.ToDictionary();
        Assert.Equal(device.DeviceId, removedAttributes["DeviceId"]);
        Assert.Equal(boardId, removedAttributes["ScopeId"]);
        Assert.True(Assert.IsType<bool>(removedAttributes["RemovedByOwnAccount"]));
        Assert.Equal(device.AccountId, TestTelemetry.FindUserId(scopeRemoved));

        var appliedId = SyncTestCommands.GetId(applied);
        Assert.DoesNotContain(logRecords, logRecord => TestTelemetry.Describe(logRecord).Contains(appliedId.ToString(), StringComparison.Ordinal));

        var span = GetRequestSpan(activities, traceId);
        Assert.Equal(device.DeviceId.ToString(), span.GetTagItem("DeviceId"));
        Assert.Equal(5, span.GetTagItem("SyncCommandCount"));
        Assert.Equal(4, span.GetTagItem("SyncProcessedCommandCount"));
        Assert.Equal(7L, span.GetTagItem("SyncPendingCommandCount"));
        Assert.Equal(42L, span.GetTagItem("SyncOldestPendingCommandAgeSeconds"));
        Assert.Equal(0, span.GetTagItem("SyncFullStateScopeCount"));
        Assert.Equal(0, span.GetTagItem("SyncRemovedScopeCount"));
        Assert.Equal(responseLength, span.GetTagItem("SyncResponseBytes"));
        Assert.False(Assert.IsType<bool>(span.GetTagItem("SyncCompressed")));
        Assert.Equal(device.AccountId.ToString(), span.GetTagItem(TestTelemetry.UserIdProperty));
    }

    [Theory]
    [InlineData("noToken", "no_token", 1)]
    [InlineData("unreadableToken", "unreadable_token", 1)]
    [InlineData("rotatedIdentity", "database_identity", 1)]
    [InlineData("unknownScope", "unknown_scope", 1)]
    [InlineData("otherEpoch", "epoch", 1)]
    [InlineData("belowTombstoneFloor", "below_floor", 1)]
    [InlineData("aboveServerVersion", "ahead_of_server", 1)]
    [InlineData("tooManyDeletions", "too_many_deletions", 1)]
    [InlineData("rootDeleteNotApplied", "root_delete_not_applied", 1)]
    [InlineData("rootDeleteWithoutToken", "no_token", 2)]
    [InlineData("rootDeleteOnUnknownScope", "root_delete_not_applied", 1)]
    [InlineData("unchanged", null, 0)]
    public async Task Sync_FullStateCondition_RecordsReason(string condition, string? expectedReason, int expectedScopeCount)
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var activities = new List<Activity>();
        var metricSnapshots = new List<MetricSnapshot>();
        var traceId = ActivityTraceId.CreateRandom();
        int mark;
        SyncResponse response;
        await using (
            var api = await ApiHost.StartAsync(
                connectionString,
                condition == "tooManyDeletions" ? new Dictionary<string, string?> { ["Sync:DataTypes:testNote:MaxRecordsPerScope"] = "1" } : null,
                activities: activities,
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var (token, dataTypes, commands) = await PrepareFullStateConditionAsync(
                condition,
                api,
                device,
                new DatabaseAdministration(connectionString)
            );
            device.Token = token;
            api.FlushMetrics();
            mark = metricSnapshots.Count;

            using var httpResponse = await device.SendAsync(
                JsonContent.Create(device.CreateBody(dataTypes, commands)),
                device.DeviceId.ToString(),
                TestTelemetry.CreateTraceParent(traceId)
            );

            response = await SyncTestDevice.ReadResponseAsync(httpResponse);
            api.FlushMetrics();
        }

        Assert.Equal(expectedScopeCount, response.Scopes.Count(block => block.FullState));
        Assert.Equal(
            expectedReason is null ? new Dictionary<string, long>() : new Dictionary<string, long> { [expectedReason] = expectedScopeCount },
            TestMetrics.SumByTags(metricSnapshots.Skip(mark), "murmli.sync.full_state.scopes", "murmli.sync.full_state.reason")
        );
        Assert.Equal(expectedScopeCount, GetRequestSpan(activities, traceId).GetTagItem("SyncFullStateScopeCount"));
    }

    [Theory]
    [InlineData("noAccount", HttpStatusCode.Gone, "account_gone")]
    [InlineData("missingContentType", HttpStatusCode.UnsupportedMediaType, "invalid_request")]
    [InlineData("nonJsonContentType", HttpStatusCode.UnsupportedMediaType, null)]
    [InlineData("invalidDeviceId", HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData("malformedBody", HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData("nullFrameMember", HttpStatusCode.BadRequest, "invalid_request")]
    [InlineData("tooManyCommands", HttpStatusCode.RequestEntityTooLarge, "request_too_large")]
    [InlineData("bodyTooLarge", HttpStatusCode.RequestEntityTooLarge, "request_too_large")]
    [InlineData("deviceRequestLimit", HttpStatusCode.TooManyRequests, "rate_limited_device")]
    [InlineData("accountRequestLimit", HttpStatusCode.TooManyRequests, "rate_limited_account")]
    [InlineData("accountCommandLimit", HttpStatusCode.TooManyRequests, "rate_limited_commands")]
    [InlineData("requestDeadline", HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    [InlineData("accountDeletedDuringRequest", HttpStatusCode.Gone, "account_gone")]
    [InlineData("lockTimeout", HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    public async Task Sync_RejectedRequest_CountsRejectionReason(string condition, HttpStatusCode expectedStatus, string? expectedRejection)
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var metricSnapshots = new List<MetricSnapshot>();
        int mark;
        await using (
            var api = await ApiHost.StartAsync(
                connectionString,
                GetRejectionConfiguration(condition),
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            var administration = new DatabaseAdministration(connectionString);
            if (condition == "accountCommandLimit")
            {
                await device.ApplyAllAsync(device.CreateNote(Guid.NewGuid(), now, "note"));
            }

            api.FlushMetrics();
            mark = metricSnapshots.Count;

            using var response = await SendRejectedRequestAsync(condition, api, device, now, administration);

            Assert.Equal(expectedStatus, response.StatusCode);
            api.FlushMetrics();
        }

        Assert.Equal(
            expectedRejection is null ? new Dictionary<string, long>() : new Dictionary<string, long> { [expectedRejection] = 1 },
            TestMetrics.SumByTags(metricSnapshots.Skip(mark), "murmli.sync.requests.rejected", "murmli.sync.rejection.reason")
        );
    }

    [Fact]
    public async Task Sync_FullStateWithGzip_RecordsUncompressedAndCompressedSize()
    {
        var activities = new List<Activity>();
        var metricSnapshots = new List<MetricSnapshot>();
        var traceId = ActivityTraceId.CreateRandom();
        int mark;
        long compressedLength;
        int uncompressedLength;
        await using (
            var api = await ApiHost.StartAsync(
                postgres.CreateDatabaseConnectionString(),
                activities: activities,
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            await device.ApplyAllAsync(device.CreateNote(Guid.NewGuid(), now, "note"));
            device.Token = null;
            api.FlushMetrics();
            mark = metricSnapshots.Count;

            using var response = await device.SendAsync(
                JsonContent.Create(device.CreateBody()),
                device.DeviceId.ToString(),
                TestTelemetry.CreateTraceParent(traceId),
                acceptEncoding: "gzip"
            );

            compressedLength = Assert.NotNull(response.Content.Headers.ContentLength);
            uncompressedLength = Encoding.UTF8.GetByteCount(await SyncTestDevice.ReadBodyAsync(response, compressed: true));
            api.FlushMetrics();
        }

        var measured = metricSnapshots.Skip(mark).ToList();
        Assert.Equal(
            new Dictionary<string, long> { ["True"] = 1 },
            TestMetrics.CountByTags(measured, "murmli.sync.response.size", "murmli.sync.response.compressed")
        );
        Assert.Equal(
            new Dictionary<string, double> { ["True"] = uncompressedLength },
            TestMetrics.HistogramSumByTags(measured, "murmli.sync.response.size", "murmli.sync.response.compressed")
        );
        Assert.Equal(
            new Dictionary<string, double> { [""] = compressedLength },
            TestMetrics.HistogramSumByTags(measured, "murmli.sync.response.compressed_size")
        );
        var span = GetRequestSpan(activities, traceId);
        Assert.Equal((long)uncompressedLength, span.GetTagItem("SyncResponseBytes"));
        Assert.True(Assert.IsType<bool>(span.GetTagItem("SyncCompressed")));
        Assert.Equal(1, span.GetTagItem("SyncFullStateScopeCount"));
    }

    [Fact]
    public async Task WriteAsync_ServerCommands_CountsWithServerWriterAndLogsNoRejection()
    {
        var logRecords = new List<LogRecord>();
        var metricSnapshots = new List<MetricSnapshot>();
        int mark;
        await using (
            var api = await ApiHost.StartAsync(
                postgres.CreateDatabaseConnectionString(),
                logRecords: logRecords,
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            api.FlushMetrics();
            mark = metricSnapshots.Count;
            var create = new SyncServerCommand(
                Guid.NewGuid(),
                SyncActor.OnBehalfOf(device.AccountId),
                SyncCommandKind.Create,
                device.PersonalScopeId,
                TestNoteType.DataTypeName,
                Guid.NewGuid(),
                new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("note") }
            );

            Assert.Equal("applied", (await api.WriteAsServerAsync(create)).Status);
            Assert.Equal("applied", (await api.WriteAsServerAsync(create)).Status);
            Assert.Equal(
                new SyncServerWriteResult("rejected", "invalid", null),
                await api.WriteAsServerAsync(create with { CommandId = Guid.NewGuid(), DataType = "unknownType" })
            );
            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long>
            {
                ["applied|none|create|testNote|server"] = 1,
                ["alreadyProcessed|none|create|testNote|server"] = 1,
                ["rejected|invalid|create|unknown|server"] = 1,
            },
            TestMetrics.SumByTags(metricSnapshots.Skip(mark), "murmli.sync.commands", CommandTags)
        );
        Assert.DoesNotContain(logRecords, logRecord => logRecord.EventId.Name == "SyncCommandRejected");
    }

    [Theory]
    [InlineData("missingTime")]
    [InlineData("unreadableTime")]
    [InlineData("nonVersion4RecordId")]
    public async Task Sync_InvalidCommandOfKnownKindAndDataType_RecordsKindAndDataType(string defect)
    {
        var logRecords = new List<LogRecord>();
        var metricSnapshots = new List<MetricSnapshot>();
        int mark;
        await using (
            var api = await ApiHost.StartAsync(
                postgres.CreateDatabaseConnectionString(),
                logRecords: logRecords,
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var note = device.CreateNote(Guid.NewGuid(), api.TimeProvider.GetUtcNow(), "note");
            var command = defect switch
            {
                "missingTime" => SyncTestCommands.Without(note, "time"),
                "unreadableTime" => SyncTestCommands.With(note, "time", "soon"),
                "nonVersion4RecordId" => SyncTestCommands.With(note, "recordId", Guid.CreateVersion7()),
                _ => throw new ArgumentOutOfRangeException(nameof(defect), defect, "The theory has no command defect with this name."),
            };
            api.FlushMetrics();
            mark = metricSnapshots.Count;

            var response = await device.SyncAsync(command);

            Assert.Equal(new SyncCommandResult(SyncTestCommands.GetId(note), "rejected", "invalid"), Assert.Single(response.Results));
            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long> { ["rejected|invalid|create|testNote|device"] = 1 },
            TestMetrics.SumByTags(metricSnapshots.Skip(mark), "murmli.sync.commands", CommandTags)
        );
        var attributes = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == "SyncCommandRejected").Attributes!.ToDictionary();
        Assert.Equal("create", attributes["CommandKind"]);
        Assert.Equal("testNote", attributes["DataType"]);
    }

    [Theory]
    [InlineData(-1L, false)]
    [InlineData(0L, true)]
    public async Task Sync_DeviceBacklog_RecordsOnlyNonNegativeValues(long backlog, bool recorded)
    {
        var activities = new List<Activity>();
        var metricSnapshots = new List<MetricSnapshot>();
        var traceId = ActivityTraceId.CreateRandom();
        int mark;
        await using (
            var api = await ApiHost.StartAsync(
                postgres.CreateDatabaseConnectionString(),
                activities: activities,
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            api.FlushMetrics();
            mark = metricSnapshots.Count;
            var body = device.CreateBody();
            body["pendingCommandCount"] = backlog;
            body["oldestPendingCommandAgeSeconds"] = backlog;

            using var response = await device.SendAsync(
                JsonContent.Create(body),
                device.DeviceId.ToString(),
                TestTelemetry.CreateTraceParent(traceId)
            );

            await SyncTestDevice.ReadResponseAsync(response);
            api.FlushMetrics();
        }

        var measured = metricSnapshots.Skip(mark).ToList();
        var expectedBacklogCounts = recorded ? new Dictionary<string, long> { [""] = 1 } : new Dictionary<string, long>();
        long? expectedBacklog = recorded ? backlog : null;
        Assert.Equal(new Dictionary<string, long> { [""] = 1 }, TestMetrics.CountByTags(measured, "murmli.sync.request.commands"));
        Assert.Equal(expectedBacklogCounts, TestMetrics.CountByTags(measured, "murmli.sync.device.pending_commands"));
        Assert.Equal(expectedBacklogCounts, TestMetrics.CountByTags(measured, "murmli.sync.device.oldest_pending_age"));
        var span = GetRequestSpan(activities, traceId);
        Assert.Equal(0, span.GetTagItem("SyncCommandCount"));
        Assert.Equal(expectedBacklog, span.GetTagItem("SyncPendingCommandCount"));
        Assert.Equal(expectedBacklog, span.GetTagItem("SyncOldestPendingCommandAgeSeconds"));
    }

    [Fact]
    public async Task Sync_TokenWithManyUnknownScopes_CountsEveryRemovedScopeAndLogsHundred()
    {
        var activities = new List<Activity>();
        var logRecords = new List<LogRecord>();
        var metricSnapshots = new List<MetricSnapshot>();
        var traceId = ActivityTraceId.CreateRandom();
        int mark;
        await using (
            var api = await ApiHost.StartAsync(
                postgres.CreateDatabaseConnectionString(),
                activities: activities,
                logRecords: logRecords,
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var (identity, _) = SyncTokenFormat.Decode(device.Token!);
            device.Token = SyncTokenFormat.Encode(
                identity,
                [
                    .. Enumerable
                        .Range(0, 200)
                        .Select(_ => (ScopeId: Guid.NewGuid(), Epoch: 0, Version: 0L))
                        .OrderBy(scope => Convert.ToHexString(scope.ScopeId.ToByteArray(bigEndian: true)), StringComparer.Ordinal),
                ]
            );
            api.FlushMetrics();
            mark = metricSnapshots.Count;

            using var httpResponse = await device.SendAsync(
                JsonContent.Create(device.CreateBody()),
                device.DeviceId.ToString(),
                TestTelemetry.CreateTraceParent(traceId)
            );

            var response = await SyncTestDevice.ReadResponseAsync(httpResponse);
            Assert.Equal(200, response.RemovedScopes.Count);
            Assert.All(response.RemovedScopes, removedScope => Assert.False(removedScope.RemovedByOwnAccount));
            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long> { ["False"] = 200 },
            TestMetrics.SumByTags(metricSnapshots.Skip(mark), "murmli.sync.scopes.removed", "murmli.sync.removed_by_own_account")
        );
        Assert.Equal(200, GetRequestSpan(activities, traceId).GetTagItem("SyncRemovedScopeCount"));
        Assert.Equal(100, logRecords.Count(logRecord => logRecord.EventId.Id == 3001));
    }

    [Fact]
    public async Task Sync_AllOperations_RecordNoContentInTelemetry()
    {
        var recorded = await RunAllOperationsAsync(recordSensitiveData: false);
        string[] contentsAndSecrets = [.. recorded.Contents, .. recorded.Secrets];

        Assert.All(recorded.Activities, activity => AssertCarriesNone(TestTelemetry.Describe(activity), contentsAndSecrets));
        Assert.All(recorded.LogRecords, logRecord => AssertCarriesNone(TestTelemetry.Describe(logRecord), contentsAndSecrets));
        Assert.All(recorded.MetricSnapshots, snapshot => AssertCarriesNone(TestTelemetry.Describe(snapshot), contentsAndSecrets));
        Assert.Contains(recorded.LogRecords, logRecord => logRecord.CategoryName == DatabaseCommandCategory);
        Assert.Contains(
            recorded.LogRecords,
            logRecord =>
                logRecord.EventId.Name == "SyncCommandRejected"
                && (logRecord.Attributes ?? []).Any(attribute => attribute is { Key: "DataType", Value: null })
        );
        var commands = TestMetrics.SumByTags(recorded.MetricSnapshots, "murmli.sync.commands", CommandTags);
        Assert.Contains("rejected|invalid|create|unknown|device", commands.Keys);
        Assert.Contains("rejected|invalid|unknown|testNote|device", commands.Keys);
    }

    [Fact]
    public async Task Sync_AllOperationsWithSensitiveDataRecorded_RecordContentOnlyInDatabaseCommandLogs()
    {
        var recorded = await RunAllOperationsAsync(recordSensitiveData: true);
        string[] contentsAndSecrets = [.. recorded.Contents, .. recorded.Secrets];

        Assert.All(recorded.Activities, activity => AssertCarriesNone(TestTelemetry.Describe(activity), contentsAndSecrets));
        Assert.All(recorded.MetricSnapshots, snapshot => AssertCarriesNone(TestTelemetry.Describe(snapshot), contentsAndSecrets));
        Assert.All(
            recorded.LogRecords.Where(logRecord => logRecord.CategoryName != DatabaseCommandCategory),
            logRecord => AssertCarriesNone(TestTelemetry.Describe(logRecord), contentsAndSecrets)
        );
        Assert.All(recorded.LogRecords, logRecord => AssertCarriesNone(TestTelemetry.Describe(logRecord), recorded.Secrets));
        Assert.Contains(
            recorded.LogRecords,
            logRecord =>
                logRecord.CategoryName == DatabaseCommandCategory
                && TestTelemetry.Describe(logRecord).Contains(recorded.Marker, StringComparison.Ordinal)
        );
    }

    private static Dictionary<string, string?> GetRejectionConfiguration(string condition) =>
        condition switch
        {
            "tooManyCommands" => new() { ["Sync:MaxCommandsPerRequest"] = "1" },
            "bodyTooLarge" => new() { ["Sync:MaxRequestBytes"] = "2000" },
            "deviceRequestLimit" => new() { ["Sync:RateLimits:DeviceRequestsPerMinute"] = "1" },
            "accountRequestLimit" => new() { ["Sync:RateLimits:AccountRequestsPerMinute"] = "1" },
            "accountCommandLimit" => new() { ["Sync:MaxCommandsPerRequest"] = "1", ["Sync:RateLimits:AccountCommandsPerHour"] = "1" },
            "requestDeadline" => new() { ["Sync:RequestDeadline"] = "00:00:01", ["Sync:LockTimeout"] = "00:00:05" },
            "lockTimeout" => new() { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
            _ => [],
        };

    private static async Task<HttpResponseMessage> SendRejectedRequestAsync(
        string condition,
        ApiHost api,
        SyncTestDevice device,
        DateTimeOffset now,
        DatabaseAdministration administration
    )
    {
        var deviceId = device.DeviceId.ToString();
        switch (condition)
        {
            case "noAccount":
                return await SyncTestDevice.WithoutAccount(api).SendAsync(JsonContent.Create(device.CreateBody()), Guid.NewGuid().ToString());
            case "missingContentType":
                return await device.SendAsync(null, deviceId);
            case "nonJsonContentType":
                return await device.SendAsync(new StringContent("{}", Encoding.UTF8, "text/plain"), deviceId);
            case "invalidDeviceId":
                return await device.SendAsync(JsonContent.Create(device.CreateBody()), "not-a-device-id");
            case "malformedBody":
                return await device.SendAsync(new StringContent("{", Encoding.UTF8, "application/json"), deviceId);
            case "nullFrameMember":
                return await device.SendAsync(
                    new StringContent("""{"dataTypes":[],"commands":null}""", Encoding.UTF8, "application/json"),
                    deviceId
                );
            case "tooManyCommands":
                return await SendNotesAsync(device, now, count: 2);
            case "bodyTooLarge":
                {
                    var body = device.CreateBody();
                    body["padding"] = new string('x', 3000);
                    return await device.SendAsync(JsonContent.Create(body), deviceId);
                }
            case "deviceRequestLimit":
                return await device.SendAsync(JsonContent.Create(device.CreateBody()), deviceId);
            case "accountRequestLimit":
                {
                    var otherDevice = device.OtherDevice();
                    return await otherDevice.SendAsync(JsonContent.Create(otherDevice.CreateBody()), otherDevice.DeviceId.ToString());
                }
            case "accountCommandLimit":
                return await SendNotesAsync(device, now, count: 1);
            case "requestDeadline" or "lockTimeout":
                {
                    await using var lockedScope = await administration.LockScopeRowAsync(device.PersonalScopeId);
                    return await SendNotesAsync(device, now, count: 1);
                }
            case "accountDeletedDuringRequest":
                {
                    await using var deletion = await administration.BeginUserDeletionAsync(device.AccountId);
                    var sync = SendNotesAsync(device, now, count: 1);
                    await administration.WaitForLockWaitersAsync(1);
                    await deletion.CommitAsync();
                    return await sync;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(condition), condition, "The theory has no request for this condition.");
        }
    }

    private static Task<HttpResponseMessage> SendNotesAsync(SyncTestDevice device, DateTimeOffset now, int count) =>
        device.SendAsync(
            JsonContent.Create(device.CreateBody([.. Enumerable.Range(0, count).Select(_ => device.CreateNote(Guid.NewGuid(), now, "note"))])),
            device.DeviceId.ToString()
        );

    private static Activity GetRequestSpan(List<Activity> activities, ActivityTraceId traceId) =>
        Assert.Single(activities, activity => activity.TraceId == traceId && activity.Kind == ActivityKind.Server);

    private static async Task<(string? Token, IReadOnlyList<string> DataTypes, JsonObject[] Commands)> PrepareFullStateConditionAsync(
        string condition,
        ApiHost api,
        SyncTestDevice device,
        DatabaseAdministration administration
    )
    {
        var versionZeroToken = device.Token!;
        var (identity, tokenScopes) = SyncTokenFormat.Decode(versionZeroToken);
        var personalScope = Assert.Single(tokenScopes);
        var now = api.TimeProvider.GetUtcNow();
        var boardId = Guid.NewGuid();
        string[] noteTypes = [TestNoteType.DataTypeName];
        switch (condition)
        {
            case "noToken":
                return (null, noteTypes, []);
            case "unreadableToken":
                return ("%%%", noteTypes, []);
            case "rotatedIdentity":
                await administration.RotateDatabaseIdentityAsync();
                return (versionZeroToken, noteTypes, []);
            case "unknownScope":
                return (SyncTokenFormat.Encode(identity, [(Guid.NewGuid(), 0, 0)]), noteTypes, []);
            case "otherEpoch":
                return (SyncTokenFormat.Encode(identity, [personalScope with { Epoch = personalScope.Epoch + 1 }]), noteTypes, []);
            case "belowTombstoneFloor":
                await device.ApplyOnOtherDeviceAsync(device.CreateNote(Guid.NewGuid(), now, "note"));
                await administration.RaiseTombstoneFloorAsync(device.PersonalScopeId, 1);
                return (versionZeroToken, noteTypes, []);
            case "aboveServerVersion":
                return (SyncTokenFormat.Encode(identity, [personalScope with { Version = 99 }]), noteTypes, []);
            case "tooManyDeletions":
                foreach (var noteId in new[] { Guid.NewGuid(), Guid.NewGuid() })
                {
                    await device.ApplyOnOtherDeviceAsync(device.CreateNote(noteId, now, "note"), device.DeleteNote(noteId, now));
                }

                return (versionZeroToken, noteTypes, []);
            case "rootDeleteNotApplied":
                await device.ApplyAllAsync(device.CreateBoard(boardId, now, "board"));
                return (device.Token, SyncTestDevice.AllDataTypes, [device.DeleteBoard(boardId, now.AddDays(1))]);
            case "rootDeleteWithoutToken":
                await device.ApplyAllAsync(device.CreateBoard(boardId, now, "board"));
                return (null, SyncTestDevice.AllDataTypes, [device.DeleteBoard(boardId, now.AddDays(1))]);
            case "rootDeleteOnUnknownScope":
                {
                    var creation = await device.OtherDevice().SyncAllAsync(device.CreateBoard(boardId, now, "board"));
                    Assert.Equal("applied", Assert.Single(creation.Results).Status);
                    return (versionZeroToken, SyncTestDevice.AllDataTypes, [device.DeleteBoard(boardId, now.AddDays(1))]);
                }
            case "unchanged":
                return (versionZeroToken, noteTypes, []);
            default:
                throw new ArgumentOutOfRangeException(nameof(condition), condition, "The theory has no full-state condition with this name.");
        }
    }

    private async Task<RecordedTelemetry> RunAllOperationsAsync(bool recordSensitiveData)
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var activities = new List<Activity>();
        var logRecords = new List<LogRecord>();
        var metricSnapshots = new List<MetricSnapshot>();
        var marker = $"marker{Guid.NewGuid():N}";
        var noteId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Telemetry:RecordSensitiveData"] = recordSensitiveData ? "true" : "false" },
            activities: activities,
            logRecords: logRecords,
            testDataTypes: true,
            metricSnapshots: metricSnapshots
        );

        var owner = await SyncTestDevice.SignInAsync(api);
        var member = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        await owner.ApplyAllAsync(
            owner.CreateNote(noteId, now, marker),
            owner.CreateBoard(boardId, now, marker),
            owner.CreateCard(boardId, cardId, now, marker)
        );
        await new DatabaseAdministration(connectionString).InsertMemberEntryAsync(boardId, member.AccountId, now, name: marker);

        using var memberHttpResponse = await member.SendAsync(
            JsonContent.Create(member.CreateBody(SyncTestDevice.AllDataTypes, [])),
            member.DeviceId.ToString(),
            acceptEncoding: "gzip"
        );
        var memberResponse = SyncTestDevice.DeserializeResponse(await SyncTestDevice.ReadBodyAsync(memberHttpResponse, compressed: true));
        var boardBlock = Assert.Single(memberResponse.Scopes, block => block.ScopeId == boardId);
        Assert.True(boardBlock.FullState);
        Assert.Contains(boardBlock.Members, boardMember => boardMember.Name == marker);
        member.Token = memberResponse.Token;
        await member.ApplyAllAsync(member.SetCardFields(boardId, cardId, now.AddSeconds(1), new JsonObject { ["text"] = $"{marker}b" }));

        var outdatedTitle = owner.SetNoteFields(noteId, now.AddSeconds(-1), SyncTestCommands.TitleFields($"{marker}c"));
        Assert.Equal("ignored", Assert.Single((await owner.SyncAllAsync(outdatedTitle)).Results).Status);

        var tooLong = owner.CreateNote(Guid.NewGuid(), now, marker + new string('x', 100));
        JsonObject[] invalidCommands =
        [
            tooLong,
            SyncTestCommands.WithField(owner.CreateNote(Guid.NewGuid(), now, "note"), marker, "x"),
            owner.CreateCommand("create", marker, owner.PersonalScopeId, Guid.NewGuid(), now, SyncTestCommands.TitleFields(marker)),
            owner.CreateCommand(marker, TestNoteType.DataTypeName, owner.PersonalScopeId, Guid.NewGuid(), now, SyncTestCommands.TitleFields(marker)),
        ];
        Assert.Equal(
            invalidCommands.Select(command => new SyncCommandResult(SyncTestCommands.GetId(command), "rejected", "invalid")),
            (await owner.SyncAllAsync(invalidCommands)).Results
        );
        Assert.Equal("alreadyProcessed", Assert.Single((await owner.SyncAllAsync(tooLong)).Results).Status);
        Assert.Equal(
            "failed",
            Assert.Single((await owner.SyncAllAsync(owner.CreateNote(Guid.NewGuid(), now, TestNoteType.FailingTitle))).Results).Status
        );

        var serverCreate = new SyncServerCommand(
            Guid.NewGuid(),
            SyncActor.OnBehalfOf(owner.AccountId),
            SyncCommandKind.Create,
            owner.PersonalScopeId,
            TestNoteType.DataTypeName,
            Guid.NewGuid(),
            new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement(marker) }
        );
        Assert.Equal("applied", (await api.WriteAsServerAsync(serverCreate)).Status);
        Assert.Equal(
            new SyncServerWriteResult("rejected", "invalid", null),
            await api.WriteAsServerAsync(serverCreate with { CommandId = Guid.NewGuid(), Actor = SyncActor.System, DataType = marker })
        );

        await owner.SyncShoppingAsync();
        var shopping = await owner.SyncShoppingAsync(
            owner.CreateList(listId, now, marker),
            owner.CreateItem(listId, itemId, now, SyncTestDevice.ItemFields(marker)),
            owner.SetItemFields(listId, itemId, now.AddSeconds(1), new JsonObject { ["checked"] = true })
        );
        Assert.Equal(["applied", "applied", "applied"], shopping.Results.Select(result => result.Status));

        api.TimeProvider.Advance(TimeSpan.FromDays(31));
        await api.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(TestContext.Current.CancellationToken);
        Assert.Contains(
            new SyncDeletedRecord(SyncTestDevice.ShoppingListItemDataType, itemId),
            Assert.Single((await owner.SyncShoppingAsync()).Scopes, block => block.ScopeId == listId).DeletedRecords
        );

        await owner.ApplyAllAsync(owner.DeleteBoard(boardId, api.TimeProvider.GetUtcNow()));
        Assert.Contains(new SyncRemovedScope(boardId, false), (await member.SyncAllAsync()).RemovedScopes);
        Assert.Equal(HttpStatusCode.NoContent, await api.DeleteMeAsync(member.AuthUserId));

        owner.Token = null;
        Assert.True((await owner.SyncAllAsync()).CoversAllScopes);
        Assert.Contains(marker, owner.ResponseBodies[^1], StringComparison.Ordinal);
        api.FlushMetrics();

        string[] syncTokens =
        [
            memberResponse.Token,
            .. owner.ResponseBodies.Concat(member.ResponseBodies).Select(body => SyncTestDevice.DeserializeResponse(body).Token),
        ];
        return new RecordedTelemetry(
            marker,
            [marker, owner.AuthUserId, member.AuthUserId],
            ["eyJhbGci", .. syncTokens.Distinct()],
            activities,
            logRecords,
            metricSnapshots
        );
    }

    private static void AssertCarriesNone(string text, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }
    }

    private sealed record RecordedTelemetry(
        string Marker,
        string[] Contents,
        string[] Secrets,
        List<Activity> Activities,
        List<LogRecord> LogRecords,
        List<MetricSnapshot> MetricSnapshots
    );
}
