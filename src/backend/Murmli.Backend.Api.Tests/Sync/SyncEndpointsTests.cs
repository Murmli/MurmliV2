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
    private const string AccountGoneType = "tag:murmli.de,2026:account-gone";

    private const string InvalidRequestType = "tag:murmli.de,2026:invalid-request";

    private const string ServiceUnavailableType = "tag:murmli.de,2026:service-unavailable";

    private const string InvalidDeviceId = "not-a-guid";

    [Fact]
    public async Task Sync_FirstSync_ReturnsCompleteFrameWithEmptyPersonalScope()
    {
        var device = await SyncTestDevice.SignInAsync(fixture.Api, firstSync: false);

        using var response = await SendBodyAsync(device, device.CreateBody());

        var syncResponse = await SyncTestDevice.ReadResponseAsync(response);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
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
        var databaseIdentity = await fixture.Api.QueryDatabaseAsync(context =>
            context.Set<DatabaseIdentity>().Select(identity => identity.Value).SingleAsync(TestContext.Current.CancellationToken)
        );
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
                CreateTraceParent(traceId)
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

            await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.Gone, AccountGoneType);
        }
    }

    [Fact]
    public async Task Sync_UnknownAccount_ReturnsAccountGone()
    {
        var device = SyncTestDevice.WithoutAccount(fixture.Api);

        using var response = await SendBodyAsync(device, device.CreateBody());

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.Gone, AccountGoneType);
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

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, InvalidRequestType);
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

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.BadRequest, InvalidRequestType);
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

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.BadRequest, InvalidRequestType);
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
    [InlineData(false, false, HttpStatusCode.Gone, AccountGoneType)]
    [InlineData(true, false, HttpStatusCode.UnsupportedMediaType, InvalidRequestType)]
    [InlineData(true, true, HttpStatusCode.BadRequest, InvalidRequestType)]
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
            var change = device.SetNoteFields(noteId, now, new JsonObject { ["title"] = "after" });

            await using (await new DatabaseAdministration(connectionString).LockScopeRowAsync(device.PersonalScopeId))
            {
                var start = Stopwatch.GetTimestamp();
                using var response = await device.SendAsync(
                    JsonContent.Create(device.CreateBody(change)),
                    device.DeviceId.ToString(),
                    CreateTraceParent(traceId)
                );

                // The lock timeout answers the same 503, but only after it and with logged retries
                var elapsed = Stopwatch.GetElapsedTime(start);
                Assert.True(elapsed < lockTimeout, $"The sync answered after {elapsed}, not before the lock timeout.");
                await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, ServiceUnavailableType);
                Assert.InRange(Assert.NotNull(response.Headers.RetryAfter?.Delta), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20));
            }

            var resent = await device.SyncAsync(change);

            Assert.Equal("applied", Assert.Single(resent.Results).Status);
            Assert.Equal(
                "after",
                await api.QueryDatabaseAsync(context =>
                    context
                        .Set<TestNote>()
                        .Where(note => note.Id == noteId)
                        .Select(note => note.Title)
                        .SingleAsync(TestContext.Current.CancellationToken)
                )
            );
        }

        Assert.DoesNotContain(
            logRecords,
            logRecord =>
                logRecord.TraceId == traceId
                && (logRecord.EventId.Name == "SyncDatabaseUnavailable" || logRecord.EventId.Name == CoreEventId.ExecutionStrategyRetrying.Name)
        );
    }

    private static Task<HttpResponseMessage> SendBodyAsync(SyncTestDevice device, JsonObject body) =>
        device.SendAsync(JsonContent.Create(body), device.DeviceId.ToString());

    private static ByteArrayContent CreateContentWithoutType(JsonObject body) => new(Encoding.UTF8.GetBytes(body.ToJsonString()));

    private static string CreateTraceParent(ActivityTraceId traceId) =>
        $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01";
}
