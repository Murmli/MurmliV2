using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncSimulationEnvironmentTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private SyncSimulationEnvironment environment = null!;

    public async ValueTask InitializeAsync() =>
        environment = await SyncSimulationEnvironment.StartAsync(postgres.CreateDatabaseConnectionString());

    public async ValueTask DisposeAsync()
    {
        if (environment is not null)
        {
            await environment.DisposeAsync();
        }
    }

    [Fact(Explicit = true)]
    public async Task ServeEnvironment_UntilCancelled_KeepsRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // dotnet test shows ITestOutputHelper output only after the test ends, also with --show-live-output on
        Console.WriteLine($"export SYNC_SIMULATION_API_URLS={string.Join(',', environment.ApiUrls)}");
        Console.WriteLine($"export SYNC_SIMULATION_CONTROL_URL={environment.Control.Url}");
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    [Fact]
    public async Task SnapshotsRestore_AfterLaterChanges_ReturnsSnapshotStateWithNewDatabaseIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var phone = await SyncTestDevice.SignInAsync(environment.Apis[0]);
        var tablet = phone.OtherDevice(environment.Apis[1]);
        var now = DateTimeOffset.FromUnixTimeMilliseconds((await GetEnvironmentAsync()).GetProperty("serverTime").GetInt64());
        var keptNoteId = Guid.NewGuid();
        var lostNoteId = Guid.NewGuid();
        var laterNoteId = Guid.NewGuid();
        await phone.ApplyAllAsync(phone.CreateNote(keptNoteId, now, "kept"));
        Assert.Equal([keptNoteId], Assert.Single((await tablet.SyncAsync()).Scopes).Records.Select(record => record.Id));
        var identityBefore = (await GetEnvironmentAsync()).GetProperty("databaseIdentity").GetGuid();

        using (var snapshotResponse = await environment.Control.HttpClient.PostAsync("/snapshots/before", content: null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, snapshotResponse.StatusCode);
        }

        await phone.ApplyAllAsync(phone.CreateNote(lostNoteId, now, "lost"));
        using (var restoreResponse = await environment.Control.HttpClient.PostAsync("/snapshots/before/restore", content: null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, restoreResponse.StatusCode);
        }

        var identityAfter = (await GetEnvironmentAsync()).GetProperty("databaseIdentity").GetGuid();
        Assert.NotEqual(identityBefore, identityAfter);
        await tablet.ApplyAllAsync(tablet.CreateNote(laterNoteId, now, "later"));
        var response = await phone.SyncAsync();
        var block = Assert.Single(response.Scopes);
        Assert.True(block.FullState);
        Assert.True(response.CoversAllScopes);
        Assert.Equal(new[] { keptNoteId, laterNoteId }.Order(), block.Records.Select(record => record.Id).Order());
        Assert.Equal(identityAfter, SyncTokenFormat.Decode(response.Token).DatabaseIdentity);
    }

    [Fact]
    public async Task ServerWrites_OnBehalfOfAccount_ReachDevicesOfBothInstances()
    {
        var phone = await SyncTestDevice.SignInAsync(environment.Apis[0]);
        var tablet = phone.OtherDevice(environment.Apis[1]);
        await tablet.SyncAsync();
        var noteId = Guid.NewGuid();
        var setFieldsRequest = CreateNoteWriteRequest(phone, noteId);
        setFieldsRequest["command"]!["kind"] = "setFields";
        setFieldsRequest["command"]!["fields"] = SyncTestCommands.TitleFields("changed");

        var createResult = await PostServerWriteAsync(CreateNoteWriteRequest(phone, noteId));
        var setFieldsResult = await PostServerWriteAsync(setFieldsRequest);
        var tabletResponse = await tablet.SyncAsync();

        var applied = new SyncSimulationServerWriteResponse("applied", null, null);
        Assert.Equal((applied, applied), (createResult, setFieldsResult));
        var block = Assert.Single(tabletResponse.Scopes);
        Assert.False(block.FullState);
        var record = Assert.Single(block.Records);
        Assert.Equal((noteId, "changed"), (record.Id, record.Fields.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task ServerWrites_InSharedScope_FillChangedByWithSubjectsEntry()
    {
        var phone = await SyncTestDevice.SignInAsync(environment.Apis[0]);
        var tablet = phone.OtherDevice(environment.Apis[1]);
        var boardId = Guid.NewGuid();
        await phone.ApplyAllAsync(phone.CreateBoard(boardId, environment.Apis[0].TimeProvider.GetUtcNow(), "board"));
        await tablet.SyncAllAsync();
        var ownerEntryId = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(environment.Apis[0], boardId)).Id;
        var cardId = Guid.NewGuid();

        var result = await PostServerWriteAsync(
            CreateServerWriteRequest(phone, boardId, TestCardType.DataTypeName, cardId, new JsonObject { ["text"] = "server" })
        );
        var tabletResponse = await tablet.SyncAllAsync();

        Assert.Equal(new SyncSimulationServerWriteResponse("applied", null, null), result);
        var block = Assert.Single(tabletResponse.Scopes, block => block.ScopeId == boardId);
        Assert.False(block.FullState);
        var record = Assert.Single(block.Records);
        Assert.Equal(
            (cardId, "server", (Guid?)ownerEntryId),
            (record.Id, record.Fields.GetProperty("text").GetString(), record.Fields.GetProperty("changedBy").Deserialize<Guid?>())
        );
    }

    [Fact]
    public async Task ServerWrites_DeleteWithoutFields_ReachesDevicesAsDeletion()
    {
        var phone = await SyncTestDevice.SignInAsync(environment.Apis[0]);
        var tablet = phone.OtherDevice(environment.Apis[1]);
        var noteId = Guid.NewGuid();
        await PostServerWriteAsync(CreateNoteWriteRequest(phone, noteId));
        await tablet.SyncAsync();
        var deleteRequest = CreateNoteWriteRequest(phone, noteId);
        deleteRequest["command"]!["kind"] = "delete";
        deleteRequest["command"]!.AsObject().Remove("fields");

        var result = await PostServerWriteAsync(deleteRequest);
        var tabletResponse = await tablet.SyncAsync();

        Assert.Equal(new SyncSimulationServerWriteResponse("applied", null, null), result);
        var block = Assert.Single(tabletResponse.Scopes);
        Assert.Empty(block.Records);
        Assert.Equal([new SyncDeletedRecord(TestNoteType.DataTypeName, noteId)], block.DeletedRecords);
    }

    [Fact]
    public async Task ServerWrites_UnknownKind_ReturnsRejectedInvalid()
    {
        var phone = await SyncTestDevice.SignInAsync(environment.Apis[0]);
        var request = CreateNoteWriteRequest(phone, Guid.NewGuid());
        request["command"]!["kind"] = "move";

        var result = await PostServerWriteAsync(request);

        Assert.Equal(new SyncSimulationServerWriteResponse("rejected", "invalid", null), result);
    }

    [Theory]
    [InlineData("subjectMissing", HttpStatusCode.BadRequest)]
    [InlineData("commandMissing", HttpStatusCode.BadRequest)]
    [InlineData("kindMissing", HttpStatusCode.BadRequest)]
    [InlineData("scopeIdMissing", HttpStatusCode.BadRequest)]
    [InlineData("dataTypeMissing", HttpStatusCode.BadRequest)]
    [InlineData("recordIdMissing", HttpStatusCode.BadRequest)]
    [InlineData("unknownSubject", HttpStatusCode.NotFound)]
    public async Task ServerWrites_IncompleteRequestOrUnknownSubject_ReturnsClientError(string scenario, HttpStatusCode expectedStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var phone = await SyncTestDevice.SignInAsync(environment.Apis[0]);
        var request = CreateNoteWriteRequest(phone, Guid.NewGuid());
        switch (scenario)
        {
            case "subjectMissing":
                request.Remove("subject");
                break;
            case "commandMissing":
                request.Remove("command");
                break;
            case "kindMissing":
                request["command"]!.AsObject().Remove("kind");
                break;
            case "scopeIdMissing":
                request["command"]!.AsObject().Remove("scopeId");
                break;
            case "dataTypeMissing":
                request["command"]!.AsObject().Remove("dataType");
                break;
            case "recordIdMissing":
                request["command"]!.AsObject().Remove("recordId");
                break;
            case "unknownSubject":
                request["subject"] = SyncTestDevice.CreateAuthUserId();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        using var response = await environment.Control.HttpClient.PostAsJsonAsync("/server-writes", request, cancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static JsonObject CreateNoteWriteRequest(SyncTestDevice device, Guid noteId) =>
        CreateServerWriteRequest(device, device.PersonalScopeId, TestNoteType.DataTypeName, noteId, SyncTestCommands.TitleFields("server"));

    private static JsonObject CreateServerWriteRequest(SyncTestDevice device, Guid scopeId, string dataType, Guid recordId, JsonObject fields) =>
        new()
        {
            ["subject"] = device.AuthUserId,
            ["command"] = new JsonObject
            {
                ["kind"] = "create",
                ["scopeId"] = scopeId,
                ["dataType"] = dataType,
                ["recordId"] = recordId,
                ["fields"] = fields,
            },
        };

    private async Task<SyncSimulationServerWriteResponse> PostServerWriteAsync(JsonObject request)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await environment.Control.HttpClient.PostAsJsonAsync("/server-writes", request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return new SyncSimulationServerWriteResponse(
            body.GetProperty("status").GetString()!,
            body.GetProperty("reason").GetString(),
            body.GetProperty("cause").GetString()
        );
    }

    private async Task<JsonElement> GetEnvironmentAsync() =>
        await environment.Control.HttpClient.GetFromJsonAsync<JsonElement>("/environment", TestContext.Current.CancellationToken);
}
