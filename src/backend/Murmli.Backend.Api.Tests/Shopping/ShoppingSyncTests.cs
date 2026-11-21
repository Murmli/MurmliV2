using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Murmli.Backend.Api.Shopping;
using Murmli.Backend.Api.Tests.Infrastructure;

namespace Murmli.Backend.Api.Tests.Shopping;

public class ShoppingSyncTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset DeviceTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    [Fact]
    public async Task SyncAsync_RetriedStaleRename_ReturnsCurrentItem()
    {
        const string authUserId = "AuthUser0000000000000000000001";
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);
        var setup = await SyncAsync(
            api,
            authUserId,
            null,
            Command("createList", DeviceTime, new { listId, name = "Groceries" }),
            Command("addItem", DeviceTime, new { itemId, listId, name = "Milk" }),
            Command("renameItem", DeviceTime.AddMinutes(2), new { itemId, name = "Oat milk" })
        );
        Assert.All(setup.Results, result => Assert.Equal("applied", result.Status));
        var staleRename = Command("renameItem", DeviceTime.AddMinutes(1), new { itemId, name = "Soy milk" });
        var firstAttempt = await SyncAsync(api, authUserId, setup.Token, staleRename);
        Assert.Equal(new SyncCommandResult(staleRename.Id, "ignored", "stale"), Assert.Single(firstAttempt.Results));

        var retry = await SyncAsync(api, authUserId, setup.Token, staleRename);

        Assert.Equal(new SyncCommandResult(staleRename.Id, "duplicate", null), Assert.Single(retry.Results));
        Assert.Contains(retry.Items, item => item.Id == itemId && item.Name == "Oat milk");
    }

    [Fact]
    public async Task SyncAsync_RetriedRejectedAddItemAfterRejoining_ReturnsDeletionMarker()
    {
        const string ownerAuthUserId = "AuthUser0000000000000000000001";
        const string guestAuthUserId = "AuthUser0000000000000000000002";
        var listId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(ownerAuthUserId);
        var guestUserId = await api.GetMeAsync(guestAuthUserId);
        await SyncAsync(api, ownerAuthUserId, null, Command("createList", DeviceTime, new { listId, name = "Groceries" }));
        await AddMemberAsync(api, ownerAuthUserId, listId, guestUserId);
        var keptToken = (await SyncAsync(api, guestAuthUserId, null)).Token;
        await RemoveMemberAsync(api, ownerAuthUserId, listId, guestUserId);
        var addItem = Command("addItem", DeviceTime.AddMinutes(1), new { itemId, listId, name = "Milk" });
        var firstAttempt = await SyncAsync(api, guestAuthUserId, keptToken, addItem);
        Assert.Equal(new SyncCommandResult(addItem.Id, "rejected", "not a member"), Assert.Single(firstAttempt.Results));
        await AddMemberAsync(api, ownerAuthUserId, listId, guestUserId);

        var retry = await SyncAsync(api, guestAuthUserId, keptToken, addItem);

        Assert.Equal(new SyncCommandResult(addItem.Id, "duplicate", null), Assert.Single(retry.Results));
        Assert.Contains(retry.Items, item => item.Id == itemId && item.Deleted);
    }

    private static SyncCommand Command(string type, DateTimeOffset occurredAt, object arguments) =>
        new(Guid.NewGuid(), type, occurredAt, JsonSerializer.SerializeToElement(arguments, JsonSerializerOptions.Web));

    private static async Task<SyncResponse> SyncAsync(ApiHost api, string authUserId, string? token, params SyncCommand[] commands)
    {
        using var response = await SendAsync(
            api,
            authUserId,
            HttpMethod.Post,
            "/v1/sync",
            JsonContent.Create(new SyncRequest(token, commands), options: JsonSerializerOptions.Web)
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var syncResponse = await response.Content.ReadFromJsonAsync<SyncResponse>(JsonSerializerOptions.Web, TestContext.Current.CancellationToken);
        return Assert.IsType<SyncResponse>(syncResponse);
    }

    private static async Task AddMemberAsync(ApiHost api, string authUserId, Guid listId, Guid userId)
    {
        using var response = await SendAsync(
            api,
            authUserId,
            HttpMethod.Post,
            $"/v1/lists/{listId}/members",
            JsonContent.Create(new { userId }, options: JsonSerializerOptions.Web)
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task RemoveMemberAsync(ApiHost api, string authUserId, Guid listId, Guid userId)
    {
        using var response = await SendAsync(api, authUserId, HttpMethod.Delete, $"/v1/lists/{listId}/members/{userId}", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(ApiHost api, string authUserId, HttpMethod method, string path, HttpContent? content)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(authUserId));
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
