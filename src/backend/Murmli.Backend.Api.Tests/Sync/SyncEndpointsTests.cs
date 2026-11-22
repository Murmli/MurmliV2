using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;
using OpenTelemetry.Logs;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    private const string InvalidDeviceId = "not-a-guid";

    [Fact]
    public async Task Sync_FirstSync_ReturnsCompleteFrameWithEmptyPersonalScope()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api, firstSync: false);

        using var response = await SendBodyAsync(device, device.CreateBody());

        var syncResponse = await SyncTestDevice.ReadResponseAsync(response);
        Assert.Empty(syncResponse.Results);
        var block = Assert.Single(syncResponse.Scopes);
        Assert.Equal(SyncScope.PersonalKind, block.Kind);
        Assert.True(block.FullState);
        Assert.Empty(block.Records);
        Assert.Empty(block.DeletedRecords);
        Assert.Empty(block.Members);
        Assert.Empty(syncResponse.RemovedScopes);
        Assert.True(syncResponse.CoversAllScopes);
        Assert.False(syncResponse.HasMore);
        Assert.Equal(fixture.Api.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(), syncResponse.ServerTime);
        Assert.Equal(new SyncSettings(60, 500, 1_000_000), syncResponse.Settings);
        var scope = await fixture.Api.QueryDatabaseAsync(context =>
            context
                .Set<SyncScope>()
                .AsNoTracking()
                .SingleAsync(scope => scope.OwnerAccountId == device.AccountId, TestContext.Current.CancellationToken)
        );
        var databaseIdentity = await new DatabaseAdministration(fixture.ConnectionString).ReadDatabaseIdentityAsync();
        var token = SyncTokenFormat.Decode(syncResponse.Token);
        Assert.Equal(databaseIdentity, token.DatabaseIdentity);
        Assert.Equal((block.ScopeId, scope.Epoch, 0L), Assert.Single(token.Scopes));
        Assert.Equal(scope.Id, block.ScopeId);
    }

    [Fact]
    public async Task Sync_CreatedNotes_ReturnAllDeviceVisibleFieldsOrderedById()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var plainNoteId = Guid.NewGuid();
        var redNoteId = Guid.NewGuid();
        var blueNoteId = Guid.NewGuid();
        var created = await device.SyncAsync(
            device.CreateNote(plainNoteId, now, "plain"),
            device.CreateNote(redNoteId, now, "red", "red", 2m),
            device.CreateNote(blueNoteId, now, "blue", "blue", 1.5m)
        );
        Assert.Equal(new[] { "applied", "applied", "applied" }, created.Results.Select(result => result.Status));

        var response = await device.OtherDevice().SyncAsync();

        var expectedFields = new Dictionary<Guid, string>
        {
            [plainNoteId] = """{"title":"plain","color":null,"weight":null,"pinned":false}""",
            [redNoteId] = """{"title":"red","color":"red","weight":2,"pinned":false}""",
            [blueNoteId] = """{"title":"blue","color":"blue","weight":1.5,"pinned":false}""",
        };
        var block = Assert.Single(response.Scopes);
        Assert.Equal(
            expectedFields.Keys.OrderBy(noteId => noteId.ToString(), StringComparer.Ordinal),
            block.Records.Select(record => record.Id)
        );
        Assert.All(
            block.Records,
            record =>
            {
                Assert.Equal(TestNoteType.DataTypeName, record.DataType);
                Assert.Equal(expectedFields[record.Id], record.Fields.GetRawText());
            }
        );
    }

    [Fact]
    public async Task Sync_UnchangedState_ReturnsSameTokenWithoutBlocks()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var token = device.Token;

        var poll = await device.SyncAsync();
        var otherDeviceResponse = await device.OtherDevice().SyncAsync();

        Assert.Equal(token, poll.Token);
        Assert.Empty(poll.Scopes);
        Assert.Equal(token, otherDeviceResponse.Token);
    }

    [Fact]
    public async Task Sync_FullStateWithGzipAccepted_ReturnsGzipOfSameJson()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var plainNoteId = Guid.NewGuid();
        var redNoteId = Guid.NewGuid();
        var created = await device.SyncAsync(
            device.CreateNote(plainNoteId, now, "plain"),
            device.CreateNote(redNoteId, now, "red", "red", 2m)
        );
        Assert.Equal(new[] { "applied", "applied" }, created.Results.Select(result => result.Status));
        var plainReader = device.OtherDevice();
        var gzipReader = device.OtherDevice();

        using var identityResponse = await SendBodyAsync(plainReader, plainReader.CreateBody());
        using var gzipResponse = await SendBodyAsync(gzipReader, gzipReader.CreateBody(), "gzip");

        Assert.Equal(4, await ReadGzipExtraFlagsAsync(gzipResponse));
        var identityBody = await SyncTestDevice.ReadBodyAsync(identityResponse, compressed: false);
        var compressedBody = await SyncTestDevice.ReadBodyAsync(gzipResponse, compressed: true);
        Assert.Equal(identityBody, compressedBody);
        var syncResponse = SyncTestDevice.DeserializeResponse(compressedBody);
        Assert.True(syncResponse.CoversAllScopes);
        var block = Assert.Single(syncResponse.Scopes);
        Assert.True(block.FullState);
        Assert.Equal(
            new[] { plainNoteId, redNoteId }.OrderBy(noteId => noteId.ToString(), StringComparer.Ordinal),
            block.Records.Select(record => record.Id)
        );
    }

    [Fact]
    public async Task Sync_FullStateCompressionLevelSmallestSize_CompressesWithThatLevel()
    {
        await using var api = await ApiHost.StartAsync(
            fixture.CreateDatabaseConnectionString(),
            new Dictionary<string, string?> { ["Sync:FullStateCompressionLevel"] = "SmallestSize" },
            testDataTypes: true
        );
        var device = await SyncTestDevice.SignInAsync(api, firstSync: false);

        using var response = await SendBodyAsync(device, device.CreateBody(), "gzip");

        Assert.Equal(2, await ReadGzipExtraFlagsAsync(response));
        await SyncTestDevice.ReadResponseAsync(response, compressed: true);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("gzip", true)]
    [InlineData("GZip", true)]
    [InlineData("deflate, gzip", true)]
    [InlineData("br;q=1.0, gzip;q=0.5", true)]
    [InlineData("gzip ; Q=0.5", true)]
    [InlineData("gzip;q=0.001", true)]
    [InlineData("gzip, gzip;q=0", true)]
    [InlineData("gzip;q=0", false)]
    [InlineData("gzip;q=0.000", false)]
    [InlineData("gzip;q=0, gzip", false)]
    [InlineData("identity", false)]
    [InlineData("identity;q=0", false)]
    [InlineData("deflate, br", false)]
    [InlineData("*", false)]
    [InlineData("x-gzip", false)]
    [InlineData("gzip;q=2", false)]
    [InlineData("gzip, br;q=bogus", false)]
    [InlineData("gzip;level=9", false)]
    [InlineData(",", false)]
    [InlineData("identity;q=1, gzip;q=0.1", true)]
    public async Task Sync_AcceptEncodingHeader_CompressesFullStateExactlyWhenGzipIsAccepted(string? acceptEncoding, bool compressed)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api, firstSync: false);

        using var response = await SendBodyAsync(device, device.CreateBody(), acceptEncoding);

        var syncResponse = await SyncTestDevice.ReadResponseAsync(response, compressed);
        Assert.True(Assert.Single(syncResponse.Scopes).FullState);
    }

    [Theory]
    [InlineData("noToken", true, 1)]
    [InlineData("noTokenWithCommand", true, 1)]
    [InlineData("tokenWithoutPersonalScope", true, 1)]
    [InlineData("tooManyDeletions", true, 1)]
    [InlineData("delta", false, 1)]
    [InlineData("includedStateOnly", false, 1)]
    [InlineData("removedScopeOnly", false, 0)]
    [InlineData("unchanged", false, 0)]
    public async Task Sync_GzipAccepted_CompressesExactlyWhenABlockHasFullState(string scenario, bool compressed, int blockCount)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);
        var now = fixture.Api.TimeProvider.GetUtcNow();
        var noteId = Guid.NewGuid();
        await device.SyncAsync(device.CreateNote(noteId, now.AddSeconds(-10), "original"));
        var tokenlessDevice = device.OtherDevice();
        var newNoteId = Guid.NewGuid();
        var otherNoteId = Guid.NewGuid();
        var foreignScopeId = Guid.NewGuid();
        var create = device.CreateNote(newNoteId, now, "new");
        var olderChange = device.SetNoteFields(noteId, now.AddSeconds(-20), SyncTestCommands.TitleFields("older"));
        var foreignCreate = device.CreateNote(Guid.NewGuid(), now, "foreign", scopeId: foreignScopeId);
        switch (scenario)
        {
            case "tokenWithoutPersonalScope":
                device.Token = SyncTokenFormat.Encode(SyncTokenFormat.Decode(device.Token!).DatabaseIdentity, [(foreignScopeId, 0, 0L)]);
                break;
            case "tooManyDeletions":
                await device.ApplyOnOtherDeviceAsync([
                    .. Enumerable
                        .Range(0, 51)
                        .Select(_ => Guid.NewGuid())
                        .SelectMany(deletedId => new[] { device.CreateNote(deletedId, now, "gone"), device.DeleteNote(deletedId, now) }),
                ]);
                break;
            case "delta":
                await device.ApplyOnOtherDeviceAsync(device.CreateNote(otherNoteId, now, "other"));
                break;
        }

        (SyncTestDevice Sender, JsonObject Body, SyncCommandResult[] Results, bool CoversAllScopes, Guid[] RecordIds, Guid[] RemovedScopeIds) expected =
            scenario switch
            {
                "noToken" => (tokenlessDevice, tokenlessDevice.CreateBody(), [], true, [noteId], []),
                "noTokenWithCommand" => (
                    tokenlessDevice,
                    tokenlessDevice.CreateBody(create),
                    [new(SyncTestCommands.GetId(create), "applied")],
                    true,
                    [.. new[] { noteId, newNoteId }.OrderBy(recordId => recordId.ToString(), StringComparer.Ordinal)],
                    []
                ),
                "tokenWithoutPersonalScope" => (device, device.CreateBody(), [], false, [noteId], [foreignScopeId]),
                "tooManyDeletions" => (device, device.CreateBody(), [], false, [noteId], []),
                "delta" => (device, device.CreateBody(), [], false, [otherNoteId], []),
                "includedStateOnly" => (
                    device,
                    device.CreateBody(olderChange),
                    [new(SyncTestCommands.GetId(olderChange), "ignored", Cause: "lostToNewer")],
                    false,
                    [noteId],
                    []
                ),
                "removedScopeOnly" => (
                    device,
                    device.CreateBody(foreignCreate),
                    [new(SyncTestCommands.GetId(foreignCreate), "rejected", "noAccess")],
                    false,
                    [],
                    [foreignScopeId]
                ),
                "unchanged" => (device, device.CreateBody(), [], false, [], []),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
            };

        using var response = await SendBodyAsync(expected.Sender, expected.Body, "gzip");

        var syncResponse = await SyncTestDevice.ReadResponseAsync(response, compressed);
        Assert.Equal(blockCount, syncResponse.Scopes.Count);
        Assert.Equal(compressed, syncResponse.Scopes.Any(block => block.FullState));
        Assert.Equal(expected.Results, syncResponse.Results);
        Assert.Equal(expected.CoversAllScopes, syncResponse.CoversAllScopes);
        Assert.Equal(expected.RecordIds, syncResponse.Scopes.SelectMany(block => block.Records).Select(record => record.Id));
        Assert.Empty(syncResponse.Scopes.SelectMany(block => block.DeletedRecords));
        Assert.Equal(expected.RemovedScopeIds.Select(scopeId => new SyncRemovedScope(scopeId, false)), syncResponse.RemovedScopes);
    }

    [Fact]
    public async Task Sync_NoChanges_AnswersWithOneStatementAfterAccountLookup()
    {
        var activities = new List<Activity>();
        var traceId = ActivityTraceId.CreateRandom();
        await using (
            var api = await ApiHost.StartAsync(fixture.CreateDatabaseConnectionString(), activities: activities, testDataTypes: true)
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);

            using var response = await device.SendAsync(
                JsonContent.Create(device.CreateBody()),
                device.DeviceId.ToString(),
                TestTelemetry.CreateTraceParent(traceId)
            );

            Assert.Empty((await SyncTestDevice.ReadResponseAsync(response)).Scopes);
        }

        Assert.Equal(
            2,
            activities.Count(activity =>
                activity.Source.Name == "Npgsql"
                && activity.TraceId == traceId
                && !activity.DisplayName.StartsWith("CONNECT", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task Sync_ConcurrentFirstSyncs_CreateExactlyOnePersonalScope()
    {
        var authUserId = SyncTestDevice.CreateAuthUserId();
        var devices = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => SyncTestDevice.SignInAsync(fixture.Api, authUserId, firstSync: false))
        );
        var administration = new DatabaseAdministration(fixture.ConnectionString);
        Task<SyncResponse>[] firstSyncs;
        await using (await administration.LockScopesTableAsync())
        {
            firstSyncs = [.. devices.Select(device => device.SyncAsync())];
            await administration.WaitForLockWaitersAsync(devices.Length);
        }

        await Task.WhenAll(firstSyncs);

        var personalScopeId = Assert.Single(devices.Select(device => device.PersonalScopeId).Distinct());
        var userId = Assert.Single(
            await fixture.Api.QueryDatabaseAsync(context =>
                context
                    .Users.Where(user => user.AuthUserId == authUserId)
                    .Select(user => user.Id)
                    .ToArrayAsync(TestContext.Current.CancellationToken)
            )
        );
        var personalScopeIds = await fixture.Api.QueryDatabaseAsync(context =>
            context
                .Set<SyncScope>()
                .Where(scope => scope.OwnerAccountId == userId && scope.Kind == SyncScope.PersonalKind)
                .Select(scope => scope.Id)
                .ToArrayAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(personalScopeId, Assert.Single(personalScopeIds));
    }

    [Fact]
    public async Task Sync_AccountDeletedWhileFirstSyncWaits_ReturnsAccountGone()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api, firstSync: false);
        var administration = new DatabaseAdministration(fixture.ConnectionString);

        await using (var deletion = await administration.BeginUserDeletionAsync(device.AccountId))
        {
            var sync = SendBodyAsync(device, device.CreateBody());
            await administration.WaitForLockWaitersAsync(1);
            await deletion.CommitAsync();

            using var response = await sync;

            await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.Gone, TestProblemTypes.AccountGone);
        }
    }

    [Fact]
    public async Task Sync_UnknownAccount_ReturnsAccountGone()
    {
        var device = SyncTestDevice.WithoutAccount(fixture.Api);

        using var response = await SendBodyAsync(device, device.CreateBody());

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.Gone, TestProblemTypes.AccountGone);
        Assert.False(
            await fixture.Api.QueryDatabaseAsync(context =>
                context.Users.AnyAsync(user => user.AuthUserId == device.AuthUserId, TestContext.Current.CancellationToken)
            )
        );
    }

    [Fact]
    public async Task Sync_ContentTypeMissing_ReturnsUnsupportedMediaTypeProblem()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);

        using var response = await device.SendAsync(CreateContentWithoutType(device.CreateBody()), device.DeviceId.ToString());

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, TestProblemTypes.InvalidRequest);
    }

    [Fact]
    public async Task Sync_ContentTypeNotJson_ReturnsUnsupportedMediaTypeWithoutBody()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);

        using var response = await device.SendAsync(
            new StringContent(device.CreateBody().ToJsonString(), Encoding.UTF8, "text/plain"),
            device.DeviceId.ToString()
        );

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(InvalidDeviceId)]
    [InlineData("6b0f3c2e7a434c4f9d0b2f1c9d6a8e11")]
    [InlineData("019aa8c4-5d2e-7b3f-9c1a-2e4f6a8b0c1d")]
    [InlineData("c232ab00-9414-11ec-b3c8-9f6bdeced846")]
    public async Task Sync_InvalidDeviceId_ReturnsInvalidRequest(string? deviceId)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);

        using var response = await device.SendAsync(JsonContent.Create(device.CreateBody()), deviceId);

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.BadRequest, TestProblemTypes.InvalidRequest);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"commands":[]}""")]
    [InlineData("""{"dataTypes":[]}""")]
    [InlineData("""{"DataTypes":[],"commands":[]}""")]
    [InlineData("""{"dataTypes":"testNote","commands":[]}""")]
    [InlineData("""{"dataTypes":[null],"commands":[]}""")]
    [InlineData("""{"dataTypes":[],"commands":null}""")]
    [InlineData("""{"dataTypes":[],"commands":[],"token":5}""")]
    [InlineData("""{"token":"\ud800","dataTypes":[],"commands":[]}""")]
    [InlineData("""{"dataTypes":["\ud800"],"commands":[]}""")]
    [InlineData("""{"dataTypes":[],"commands":[],"\ud800":1}""")]
    [InlineData("""{"dataTypes":[],"commands":[],"pendingCommandCount":"x"}""")]
    [InlineData("""{"dataTypes":[],"commands":[],"pendingCommandCount":"5"}""")]
    public async Task Sync_MalformedEnvelope_ReturnsInvalidRequest(string body)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);

        using var response = await device.SendAsync(new StringContent(body, Encoding.UTF8, "application/json"), device.DeviceId.ToString());

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.BadRequest, TestProblemTypes.InvalidRequest);
    }

    [Theory]
    [InlineData("""{"dataTypes":[],"commands":[],"appVersion":"1.0"}""")]
    [InlineData("""{"dataTypes":[],"commands":[],"appVersion":"\ud800"}""")]
    public async Task Sync_UnknownEnvelopeMember_IsIgnored(string body)
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api);

        using var response = await device.SendAsync(new StringContent(body, Encoding.UTF8, "application/json"), device.DeviceId.ToString());

        await SyncTestDevice.ReadResponseAsync(response);
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.Gone, TestProblemTypes.AccountGone)]
    [InlineData(true, false, HttpStatusCode.UnsupportedMediaType, TestProblemTypes.InvalidRequest)]
    [InlineData(true, true, HttpStatusCode.BadRequest, TestProblemTypes.InvalidRequest)]
    public async Task Sync_SeveralChecksFail_ReturnsFirstFailureInDocumentedOrder(
        bool hasAccount,
        bool hasJsonContentType,
        HttpStatusCode expectedStatusCode,
        string expectedProblemType
    )
    {
        var device = hasAccount ? await SyncTestDevice.SignInAsync(fixture.Api) : SyncTestDevice.WithoutAccount(fixture.Api);
        var body = device.CreateBody([.. Enumerable.Range(0, 501).Select(_ => new JsonObject())]);

        using var response = await device.SendAsync(
            hasJsonContentType ? JsonContent.Create(body) : CreateContentWithoutType(body),
            InvalidDeviceId
        );

        await SyncTestDevice.AssertProblemAsync(response, expectedStatusCode, expectedProblemType);
    }

    [Fact]
    public async Task Sync_RequestDeadlineExceeded_ReturnsServiceUnavailable()
    {
        var lockTimeout = TimeSpan.FromSeconds(5);
        var logRecords = new List<LogRecord>();
        var traceId = ActivityTraceId.CreateRandom();
        var connectionString = fixture.CreateDatabaseConnectionString();
        await using (
            var api = await ApiHost.StartAsync(
                connectionString,
                new Dictionary<string, string?> { ["Sync:RequestDeadline"] = "00:00:01", ["Sync:LockTimeout"] = lockTimeout.ToString() },
                logRecords: logRecords,
                testDataTypes: true
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var noteId = Guid.NewGuid();
            var now = api.TimeProvider.GetUtcNow();
            await device.SyncAsync(device.CreateNote(noteId, now.AddSeconds(-10), "before"));
            var change = device.SetNoteFields(noteId, now, SyncTestCommands.TitleFields("after"));

            await using (await new DatabaseAdministration(connectionString).LockScopeRowAsync(device.PersonalScopeId))
            {
                var start = Stopwatch.GetTimestamp();
                using var response = await device.SendAsync(
                    JsonContent.Create(device.CreateBody(change)),
                    device.DeviceId.ToString(),
                    TestTelemetry.CreateTraceParent(traceId)
                );

                // The lock timeout answers the same 503, but only after it and with logged retries
                var elapsed = Stopwatch.GetElapsedTime(start);
                Assert.True(elapsed < lockTimeout, $"The sync answered after {elapsed}, not before the lock timeout.");
                await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, TestProblemTypes.ServiceUnavailable);
                Assert.InRange(Assert.NotNull(response.Headers.RetryAfter?.Delta), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20));
            }

            var resent = await device.SyncAsync(change);

            Assert.Equal("applied", Assert.Single(resent.Results).Status);
            Assert.Equal("after", await TestNoteQueries.ReadTitleAsync(api, noteId));
        }

        Assert.DoesNotContain(
            logRecords,
            logRecord =>
                logRecord.TraceId == traceId
                && (logRecord.EventId.Name == "SyncDatabaseUnavailable" || logRecord.EventId.Name == CoreEventId.ExecutionStrategyRetrying.Name)
        );
    }

    private static Task<HttpResponseMessage> SendBodyAsync(SyncTestDevice device, JsonObject body, string? acceptEncoding = null) =>
        device.SendAsync(JsonContent.Create(body), device.DeviceId.ToString(), acceptEncoding: acceptEncoding);

    private static ByteArrayContent CreateContentWithoutType(JsonObject body) => new(Encoding.UTF8.GetBytes(body.ToJsonString()));

    // XFL of the gzip header (RFC 1952), set from the compression level: 4 for Fastest, 2 for SmallestSize
    private static async Task<byte> ReadGzipExtraFlagsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken))[8];
}
