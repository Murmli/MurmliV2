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

    private static async Task<Guid> GetMeAsync(ApiHost api, string zitadelUserId)
    {
        using var response = await SendGetMeAsync(api, TestTokens.Create(zitadelUserId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(TestContext.Current.CancellationToken);
        return Assert.IsType<UserResponse>(user).Id;
    }

    private static async Task<HttpResponseMessage> SendGetMeAsync(ApiHost api, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<User[]> ReadUsersAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.Users.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken));
}
