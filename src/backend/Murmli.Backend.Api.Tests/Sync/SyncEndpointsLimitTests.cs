using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsLimitTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Sync_TooManyCommands_RejectsWholeRequestAndProcessesNothing()
    {
        await using var api = await StartApiAsync("Sync:MaxCommandsPerRequest", "2");
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var body = device.CreateBody(
            device.CreateNote(Guid.NewGuid(), now, "first"),
            device.CreateNote(Guid.NewGuid(), now, "second"),
            device.CreateNote(Guid.NewGuid(), now, "third")
        );

        using var response = await device.SendAsync(JsonContent.Create(body), device.DeviceId.ToString());

        var problem = await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, TestProblemTypes.RequestTooLarge);
        Assert.Equal(2, problem.GetProperty("maxCommandsPerRequest").GetInt32());
        Assert.Equal(1_000_000, problem.GetProperty("maxRequestBytes").GetInt32());
        await AssertNothingProcessedAsync(api, device);
        Assert.Equal(2, (await device.SyncAsync()).Settings.MaxCommandsPerRequest);
    }

    [Fact]
    public async Task Sync_BodyTooLarge_RejectsWholeRequest()
    {
        await using var api = await StartApiAsync("Sync:MaxRequestBytes", "2000");
        var device = await SyncTestDevice.SignInAsync(api);
        var body = device.CreateBody(device.CreateNote(Guid.NewGuid(), api.TimeProvider.GetUtcNow(), "note"));
        body["padding"] = new string('x', 3000);

        using var response = await device.SendAsync(JsonContent.Create(body), device.DeviceId.ToString());

        var problem = await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, TestProblemTypes.RequestTooLarge);
        Assert.Equal(2000, problem.GetProperty("maxRequestBytes").GetInt32());
        await AssertNothingProcessedAsync(api, device);
        Assert.Equal(2000, (await device.SyncAsync()).Settings.MaxRequestBytes);
    }

    [Fact]
    public async Task Sync_CreateAtRecordLimit_RejectedLimitExceeded()
    {
        await using var api = await StartApiAsync("Sync:DataTypes:testNote:MaxRecordsPerScope", "2");
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        Guid[] noteIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        var response = await device.SyncAsync(
            device.CreateNote(noteIds[0], now, "first"),
            device.CreateNote(noteIds[1], now, "second"),
            device.CreateNote(noteIds[2], now, "third")
        );

        Assert.Equal(
            new (string, string?)[] { ("applied", null), ("applied", null), ("rejected", "limitExceeded") },
            response.Results.Select(result => (result.Status, result.Reason))
        );
        var block = Assert.Single(response.Scopes);
        Assert.False(block.FullState);
        Assert.Equal(noteIds[..2].OrderBy(noteId => noteId.ToString(), StringComparer.Ordinal), block.Records.Select(record => record.Id));
        Assert.Equal(new SyncDeletedRecord(TestNoteType.DataTypeName, noteIds[2]), Assert.Single(block.DeletedRecords));
    }

    [Fact]
    public async Task Sync_ConcurrentCreatesAtRecordLimit_DoNotExceedLimit()
    {
        await using var api = await StartApiAsync("Sync:DataTypes:testNote:MaxRecordsPerScope", "3");
        var authUserId = SyncTestDevice.CreateAuthUserId();
        var devices = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => SyncTestDevice.SignInAsync(api, authUserId)));
        var now = api.TimeProvider.GetUtcNow();

        var responses = await Task.WhenAll(devices.Select(device => device.SyncAsync(device.CreateNote(Guid.NewGuid(), now, "note"))));

        var results = responses.Select(response => Assert.Single(response.Results)).ToArray();
        Assert.Equal(3, results.Count(result => result.Status == "applied"));
        Assert.Equal(7, results.Count(result => result is { Status: "rejected", Reason: "limitExceeded" }));
        Assert.Equal(3, await TestNoteQueries.CountInScopeAsync(api, devices[0].PersonalScopeId));
    }

    [Fact]
    public async Task Sync_RootCreateAtOwnedScopeLimit_RejectedLimitExceededAndReportedRemoved()
    {
        await using var api = await StartApiAsync("Sync:DataTypes:testBoard:MaxOwnedScopesPerAccount", "2");
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        Guid[] boardIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        var response = await device.SyncAllAsync([.. boardIds.Select(boardId => device.CreateBoard(boardId, now, "board"))]);
        var ownedScopeCountAtLimit = await SharedScopeQueries.CountOwnedScopesAsync(api, device.AccountId, TestBoardType.ScopeKindName);
        var afterDelete = await device.SyncAllAsync(device.DeleteBoard(boardIds[0], now), device.CreateBoard(boardIds[2], now, "board"));

        Assert.Equal(
            new (string, string?)[] { ("applied", null), ("applied", null), ("rejected", "limitExceeded") },
            response.Results.Select(result => (result.Status, result.Reason))
        );
        Assert.Equal(new SyncRemovedScope(boardIds[2], false), Assert.Single(response.RemovedScopes));
        Assert.Equal(2, ownedScopeCountAtLimit);
        Assert.Equal(new[] { "applied", "applied" }, afterDelete.Results.Select(result => result.Status));
        Assert.Equal(2, await SharedScopeQueries.CountOwnedScopesAsync(api, device.AccountId, TestBoardType.ScopeKindName));
    }

    [Fact]
    public async Task Sync_ConcurrentScopeCreationsAtLimit_DoNotExceedLimit()
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:DataTypes:testBoard:MaxOwnedScopesPerAccount"] = "3" },
            testDataTypes: true
        );
        var authUserId = SyncTestDevice.CreateAuthUserId();
        var devices = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => SyncTestDevice.SignInAsync(api, authUserId)));
        var now = api.TimeProvider.GetUtcNow();
        var administration = new DatabaseAdministration(connectionString);
        Task<SyncResponse>[] creates;
        await using (await administration.LockScopesTableAsync())
        {
            creates = [.. devices.Select(device => device.SyncAllAsync(device.CreateBoard(Guid.NewGuid(), now, "board")))];
            await administration.WaitForLockWaitersAsync(devices.Length);
        }

        var responses = await Task.WhenAll(creates);

        var results = responses.Select(response => Assert.Single(response.Results)).ToArray();
        Assert.Equal(3, results.Count(result => result.Status == "applied"));
        Assert.Equal(7, results.Count(result => result is { Status: "rejected", Reason: "limitExceeded" }));
        Assert.Equal(3, await SharedScopeQueries.CountOwnedScopesAsync(api, devices[0].AccountId, TestBoardType.ScopeKindName));
    }

    [Fact]
    public async Task Sync_CommandPhaseBudgetExceeded_ReturnsResultsForProcessedPrefix()
    {
        await using var api = await StartApiAsync("Sync:CommandPhaseBudget", "00:00:00");
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();
        var commands = Enumerable.Range(0, 3).Select(_ => device.CreateNote(Guid.NewGuid(), now, "note")).ToArray();

        var first = await device.SyncAsync(commands);
        var second = await device.SyncAsync(commands[1..]);
        var third = await device.SyncAsync(commands[2..]);

        Assert.Equal(
            commands.Select(command => ((Guid?)SyncTestCommands.GetId(command), "applied")),
            new[] { first, second, third }.Select(response => Assert.Single(response.Results)).Select(result => (result.CommandId, result.Status))
        );
        Assert.Equal(3, await TestNoteQueries.CountInScopeAsync(api, device.PersonalScopeId));
    }

    [Fact]
    public async Task Sync_DeviceRequestLimitExceeded_ReturnsTooManyRequestsWithRetryAfter()
    {
        await using var api = await StartApiAsync(
            new Dictionary<string, string?> { ["Sync:RateLimits:DeviceRequestsPerMinute"] = "2", ["Sync:RateLimits:AccountRequestsPerMinute"] = "3" }
        );
        var windowStart = Stopwatch.GetTimestamp();
        var device = await SyncTestDevice.SignInAsync(api);
        await device.SyncAsync();

        using var response = await SendNoteAsync(api, device);

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.TooManyRequests, TestProblemTypes.RateLimited);
        AssertRetryAfterUntilWindowEnd(response, windowStart);
        await AssertNothingProcessedAsync(api, device);
        await device.OtherDevice().SyncAsync();
        var otherAccount = await SyncTestDevice.SignInAsync(api);
        using var sameDeviceIdOfOtherAccount = await otherAccount.SendAsync(
            JsonContent.Create(otherAccount.CreateBody()),
            device.DeviceId.ToString()
        );
        await SyncTestDevice.ReadResponseAsync(sameDeviceIdOfOtherAccount);
    }

    [Fact]
    public async Task Sync_AccountRequestLimitExceeded_ReturnsTooManyRequestsWithRetryAfter()
    {
        await using var api = await StartApiAsync("Sync:RateLimits:AccountRequestsPerMinute", "2");
        var windowStart = Stopwatch.GetTimestamp();
        var device = await SyncTestDevice.SignInAsync(api);
        await device.OtherDevice().SyncAsync();

        using var response = await SendNoteAsync(api, device.OtherDevice());

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.TooManyRequests, TestProblemTypes.RateLimited);
        AssertRetryAfterUntilWindowEnd(response, windowStart);
        await AssertNothingProcessedAsync(api, device);
        await SyncTestDevice.SignInAsync(api);
    }

    [Fact]
    public async Task Sync_AccountCommandLimitExceeded_ReturnsTooManyRequests()
    {
        await using var api = await StartApiAsync(
            new Dictionary<string, string?> { ["Sync:MaxCommandsPerRequest"] = "4", ["Sync:RateLimits:AccountCommandsPerHour"] = "4" }
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var otherDevice = device.OtherDevice();
        var now = api.TimeProvider.GetUtcNow();
        await device.ApplyAllAsync(CreateNotes(device, now, 3));

        using var response = await otherDevice.SendAsync(
            JsonContent.Create(otherDevice.CreateBody(CreateNotes(otherDevice, now, 4))),
            otherDevice.DeviceId.ToString()
        );

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.TooManyRequests, TestProblemTypes.RateLimited);
        Assert.Equal(TimeSpan.FromMinutes(3), response.Headers.RetryAfter?.Delta);
        Assert.Equal(3, await TestNoteQueries.CountInScopeAsync(api, device.PersonalScopeId));
        Assert.Equal(3, (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId)).Count);
        await otherDevice.ApplyAllAsync(CreateNotes(otherDevice, now, 1));
        var otherAccount = await SyncTestDevice.SignInAsync(api);
        await otherAccount.ApplyAllAsync(CreateNotes(otherAccount, now, 4));
    }

    [Fact]
    public async Task Sync_CommandLimitReached_AnswersRequestWithoutCommands()
    {
        await using var api = await StartApiAsync(
            new Dictionary<string, string?> { ["Sync:MaxCommandsPerRequest"] = "2", ["Sync:RateLimits:AccountCommandsPerHour"] = "2" }
        );
        var device = await SyncTestDevice.SignInAsync(api);
        await device.ApplyAllAsync(CreateNotes(device, api.TimeProvider.GetUtcNow(), 2));
        using var rejected = await SendNoteAsync(api, device);

        await device.SyncAsync();

        await SyncTestDevice.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, TestProblemTypes.RateLimited);
        Assert.Equal(TimeSpan.FromMinutes(1), rejected.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Sync_TooManyCommandsAtCommandLimit_RejectsWithoutTakingCommandPermits()
    {
        await using var api = await StartApiAsync(
            new Dictionary<string, string?> { ["Sync:MaxCommandsPerRequest"] = "2", ["Sync:RateLimits:AccountCommandsPerHour"] = "3" }
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var now = api.TimeProvider.GetUtcNow();

        using var tooLarge = await device.SendAsync(JsonContent.Create(device.CreateBody(CreateNotes(device, now, 3))), device.DeviceId.ToString());

        await SyncTestDevice.AssertProblemAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, TestProblemTypes.RequestTooLarge);
        await device.ApplyAllAsync(CreateNotes(device, now, 2));
    }

    [Fact]
    public async Task Sync_OversizedBodyBeyondDeviceRequestLimit_ReturnsTooManyRequestsBeforeReadingBody()
    {
        await using var api = await StartApiAsync(
            new Dictionary<string, string?> { ["Sync:RateLimits:DeviceRequestsPerMinute"] = "2", ["Sync:MaxRequestBytes"] = "2000" }
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var body = device.CreateBody(device.CreateNote(Guid.NewGuid(), api.TimeProvider.GetUtcNow(), "note"));
        body["padding"] = new string('x', 3000);
        using var tooLarge = await device.SendAsync(JsonContent.Create(body), device.DeviceId.ToString());

        using var rejected = await device.SendAsync(JsonContent.Create(body), device.DeviceId.ToString());

        await SyncTestDevice.AssertProblemAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, TestProblemTypes.RequestTooLarge);
        await SyncTestDevice.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, TestProblemTypes.RateLimited);
    }

    [Fact]
    public async Task Sync_AccountRequestLimitReachedOnOneInstance_OtherInstanceAccepts()
    {
        var connectionString = postgres.CreateDatabaseConnectionString();
        var configuration = new Dictionary<string, string?> { ["Sync:RateLimits:AccountRequestsPerMinute"] = "1" };
        await using var firstApi = await ApiHost.StartAsync(connectionString, configuration, testDataTypes: true);
        await using var secondApi = await ApiHost.StartAsync(connectionString, configuration, testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(firstApi);
        using var rejected = await SendNoteAsync(firstApi, device.OtherDevice());

        await device.OtherDevice(secondApi).SyncAsync();

        await SyncTestDevice.AssertProblemAsync(rejected, HttpStatusCode.TooManyRequests, TestProblemTypes.RateLimited);
    }

    private static JsonObject[] CreateNotes(SyncTestDevice device, DateTimeOffset time, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => device.CreateNote(Guid.NewGuid(), time, "note"))];

    private static Task<HttpResponseMessage> SendNoteAsync(ApiHost api, SyncTestDevice device) =>
        device.SendAsync(
            JsonContent.Create(device.CreateBody(device.CreateNote(Guid.NewGuid(), api.TimeProvider.GetUtcNow(), "note"))),
            device.DeviceId.ToString()
        );

    // The window of a new partition starts at its first request, which comes after windowStart
    private static void AssertRetryAfterUntilWindowEnd(HttpResponseMessage response, long windowStart) =>
        Assert.InRange(
            Assert.NotNull(response.Headers.RetryAfter?.Delta),
            TimeSpan.FromMinutes(1) - Stopwatch.GetElapsedTime(windowStart),
            TimeSpan.FromMinutes(1)
        );

    private static async Task AssertNothingProcessedAsync(ApiHost api, SyncTestDevice device)
    {
        Assert.Equal(0, await TestNoteQueries.CountInScopeAsync(api, device.PersonalScopeId));
        Assert.Empty(await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId));
    }

    private Task<ApiHost> StartApiAsync(string key, string value) => StartApiAsync(new Dictionary<string, string?> { [key] = value });

    private Task<ApiHost> StartApiAsync(IReadOnlyDictionary<string, string?> configuration) =>
        ApiHost.StartAsync(postgres.CreateDatabaseConnectionString(), configuration, testDataTypes: true);
}
