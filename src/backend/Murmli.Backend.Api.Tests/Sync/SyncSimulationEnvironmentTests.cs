using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;

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

    private async Task<JsonElement> GetEnvironmentAsync() =>
        await environment.Control.HttpClient.GetFromJsonAsync<JsonElement>("/environment", TestContext.Current.CancellationToken);
}
