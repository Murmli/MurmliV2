using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Users;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Api.Tests.Users;

public class UserEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    [Fact]
    public async Task GetMe_WithoutToken_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await api.HttpClient.GetAsync("/users/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_TokenForOtherAudience_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await SendGetMeAsync(api, TestTokens.Create("100000000000000001", "other-project"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_FirstRequest_CreatesUser()
    {
        const string zitadelUserId = "100000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);

        var userId = await GetMeAsync(api, zitadelUserId);

        var user = Assert.Single(await ReadUsersAsync(api));
        Assert.Equal(zitadelUserId, user.ZitadelUserId);
        Assert.Equal(user.Id, userId);
    }

    [Fact]
    public async Task GetMe_ConcurrentFirstRequests_CreateOneUser()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        var userIds = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => GetMeAsync(api, "100000000000000001")));

        var user = Assert.Single(await ReadUsersAsync(api));
        Assert.All(userIds, userId => Assert.Equal(user.Id, userId));
    }

    [Fact]
    public async Task GetMe_RepeatedRequest_ReturnsSameUser()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        var firstUserId = await GetMeAsync(api, "100000000000000001");

        var secondUserId = await GetMeAsync(api, "100000000000000001");

        Assert.Equal(firstUserId, secondUserId);
        Assert.Single(await ReadUsersAsync(api));
    }

    [Fact]
    public async Task DeleteMe_ExistingUser_DeletesUserAndZitadelUser()
    {
        const string zitadelUserId = "100000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await GetMeAsync(api, zitadelUserId);

        var statusCode = await DeleteMeAsync(api, TestTokens.Create(zitadelUserId));

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Empty(await ReadUsersAsync(api));
        var deletedUser = Assert.Single(await ReadDeletedUsersAsync(api));
        Assert.Equal(zitadelUserId, deletedUser.ZitadelUserId);
        var request = Assert.Single(api.Zitadel.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/v2/users/{zitadelUserId}", request.Path);
        Assert.Equal($"Bearer {ApiHost.ZitadelServiceUserToken}", request.Authorization);
    }

    [Fact]
    public async Task DeleteMe_ZitadelFails_AllowsRetryWithSameToken()
    {
        const string zitadelUserId = "100000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await GetMeAsync(api, zitadelUserId);
        var token = TestTokens.Create(zitadelUserId);
        api.Zitadel.ResponseStatusCode = HttpStatusCode.InternalServerError;
        Assert.Equal(HttpStatusCode.BadGateway, await DeleteMeAsync(api, token));
        api.Zitadel.ResponseStatusCode = HttpStatusCode.OK;

        var statusCode = await DeleteMeAsync(api, token);

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Equal(2, api.Zitadel.Requests.Count);
        Assert.Empty(await ReadUsersAsync(api));
    }

    [Fact]
    public async Task DeleteMe_UserAlreadyDeletedInZitadel_ReturnsNoContent()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        api.Zitadel.ResponseStatusCode = HttpStatusCode.NotFound;

        var statusCode = await DeleteMeAsync(api, TestTokens.Create("100000000000000001"));

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.Unauthorized, 0)]
    [InlineData(1, HttpStatusCode.OK, 1)]
    public async Task GetMe_AfterDeletion_DependsOnIssuedAt(
        int issuedMinutesAfterDeletion,
        HttpStatusCode expectedStatusCode,
        int expectedUserCount
    )
    {
        const string zitadelUserId = "100000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await GetMeAsync(api, zitadelUserId);
        var deletedAt = api.TimeProvider.GetUtcNow();
        await DeleteMeAsync(api, TestTokens.Create(zitadelUserId));
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        var token = TestTokens.Create(zitadelUserId, issuedAt: deletedAt.AddMinutes(issuedMinutesAfterDeletion));

        using var response = await SendGetMeAsync(api, token);

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal(expectedUserCount, (await ReadUsersAsync(api)).Length);
    }

    [Fact]
    public async Task DeleteMe_OldDeletionRecords_AreRemoved()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await DeleteMeAsync(api, TestTokens.Create("100000000000000001"));
        api.TimeProvider.Advance(TimeSpan.FromHours(25));

        await DeleteMeAsync(api, TestTokens.Create("100000000000000002"));

        var deletedUser = Assert.Single(await ReadDeletedUsersAsync(api));
        Assert.Equal("100000000000000002", deletedUser.ZitadelUserId);
    }

    private static async Task<Guid> GetMeAsync(ApiHost api, string zitadelUserId)
    {
        using var response = await SendGetMeAsync(api, TestTokens.Create(zitadelUserId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(TestContext.Current.CancellationToken);
        return Assert.IsType<UserResponse>(user).Id;
    }

    private static Task<HttpResponseMessage> SendGetMeAsync(ApiHost api, string token) => SendAsync(api, HttpMethod.Get, token);

    private static async Task<HttpStatusCode> DeleteMeAsync(ApiHost api, string token)
    {
        using var response = await SendAsync(api, HttpMethod.Delete, token);
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> SendAsync(ApiHost api, HttpMethod method, string token)
    {
        using var request = new HttpRequestMessage(method, "/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<User[]> ReadUsersAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.Users.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken));

    private static Task<DeletedUser[]> ReadDeletedUsersAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.DeletedUsers.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken));
}
