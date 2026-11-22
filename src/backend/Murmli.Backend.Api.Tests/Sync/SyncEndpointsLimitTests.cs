using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsLimitTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string RequestTooLargeType = "tag:murmli.de,2026:request-too-large";

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

        var problem = await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, RequestTooLargeType);
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

        var problem = await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, RequestTooLargeType);
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
        Assert.Equal(3, await CountNotesAsync(api, devices[0].PersonalScopeId));
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
            commands.Select(command => ((Guid?)command["id"]!.GetValue<Guid>(), "applied")),
            new[] { first, second, third }.Select(response => Assert.Single(response.Results)).Select(result => (result.CommandId, result.Status))
        );
        Assert.Equal(3, await CountNotesAsync(api, device.PersonalScopeId));
    }

    private static Task<int> CountNotesAsync(ApiHost api, Guid scopeId) =>
        api.QueryDatabaseAsync(context =>
            context.Set<TestNote>().CountAsync(note => note.ScopeId == scopeId, TestContext.Current.CancellationToken)
        );

    private static async Task AssertNothingProcessedAsync(ApiHost api, SyncTestDevice device)
    {
        Assert.Equal(0, await CountNotesAsync(api, device.PersonalScopeId));
        Assert.False(
            await api.QueryDatabaseAsync(context =>
                context
                    .Set<ProcessedSyncCommand>()
                    .AnyAsync(command => command.AccountId == device.AccountId, TestContext.Current.CancellationToken)
            )
        );
    }

    private Task<ApiHost> StartApiAsync(string key, string value) =>
        ApiHost.StartAsync(
            postgres.CreateDatabaseConnectionString(),
            new Dictionary<string, string?> { [key] = value },
            testDataTypes: true
        );
}
