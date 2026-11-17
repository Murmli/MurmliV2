using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Api.Tests.Users;

public class UserEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    public static TheoryData<Exception> AuthServiceTransportFailures =>
        new()
        {
            // HttpClient reports its own timeout as a TaskCanceledException wrapping a TimeoutException
            new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.",
                new TimeoutException()
            ),
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "Connection refused (auth.test:80)",
                new SocketException((int)SocketError.ConnectionRefused)
            ),
        };

    [Fact]
    public async Task GetMe_WithoutToken_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await api.HttpClient.GetAsync("/v1/users/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_TokenForOtherAudience_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await SendGetMeAsync(api, TestTokens.Create("AuthUser0000000000000000000001", "other-audience"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_FirstRequest_CreatesUser()
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);

        var userId = await api.GetMeAsync(authUserId);

        var user = Assert.Single(await ReadUsersAsync(api));
        Assert.Equal(authUserId, user.AuthUserId);
        Assert.Equal(user.Id, userId);
    }

    [Fact]
    public async Task GetMe_ConcurrentFirstRequests_CreateOneUser()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        var userIds = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => api.GetMeAsync("AuthUser0000000000000000000001")));

        var user = Assert.Single(await ReadUsersAsync(api));
        Assert.All(userIds, userId => Assert.Equal(user.Id, userId));
    }

    [Fact]
    public async Task GetMe_RepeatedRequest_ReturnsSameUser()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        var firstUserId = await api.GetMeAsync("AuthUser0000000000000000000001");

        var secondUserId = await api.GetMeAsync("AuthUser0000000000000000000001");

        Assert.Equal(firstUserId, secondUserId);
        Assert.Single(await ReadUsersAsync(api));
    }

    [Fact]
    public async Task DeleteMe_ExistingUser_DeletesUserAndAuthUser()
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);

        var statusCode = await DeleteMeAsync(api, TestTokens.Create(authUserId));

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Empty(await ReadUsersAsync(api));
        var deletedUser = Assert.Single(await ReadDeletedUsersAsync(api));
        Assert.Equal(authUserId, deletedUser.AuthUserId);
        var request = Assert.Single(api.AuthService.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/service/delete-user", request.Path);
        Assert.Equal($"Bearer {ApiHost.AuthServiceKey}", request.Authorization);
        Assert.NotNull(request.Body);
        Assert.Equal(
            new Dictionary<string, string> { ["userId"] = authUserId },
            JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body, JsonSerializerOptions.Web)
        );
    }

    [Fact]
    public async Task DeleteMe_AuthServiceFails_AllowsRetryWithSameToken()
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);
        var token = TestTokens.Create(authUserId);
        api.AuthService.ResponseStatusCode = HttpStatusCode.InternalServerError;
        Assert.Equal(HttpStatusCode.BadGateway, await DeleteMeAsync(api, token));
        api.AuthService.ResponseStatusCode = HttpStatusCode.NoContent;

        var statusCode = await DeleteMeAsync(api, token);

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Equal(2, api.AuthService.Requests.Count);
        Assert.Empty(await ReadUsersAsync(api));
    }

    [Theory]
    [MemberData(nameof(AuthServiceTransportFailures))]
    public async Task DeleteMe_AuthServiceDoesNotAnswer_AllowsRetryWithSameToken(Exception transportFailure)
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);
        var token = TestTokens.Create(authUserId);
        api.AuthService.TransportFailure = transportFailure;
        Assert.Equal(HttpStatusCode.BadGateway, await DeleteMeAsync(api, token));
        api.AuthService.TransportFailure = null;

        var statusCode = await DeleteMeAsync(api, token);

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Equal(2, api.AuthService.Requests.Count);
        Assert.Empty(await ReadUsersAsync(api));
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
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);
        var deletedAt = api.TimeProvider.GetUtcNow();
        await DeleteMeAsync(api, TestTokens.Create(authUserId));
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        var token = TestTokens.Create(authUserId, issuedAt: deletedAt.AddMinutes(issuedMinutesAfterDeletion));

        using var response = await SendGetMeAsync(api, token);

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal(expectedUserCount, (await ReadUsersAsync(api)).Length);
    }

    [Fact]
    public async Task DeleteMe_OldDeletionRecords_AreRemoved()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await DeleteMeAsync(api, TestTokens.Create("AuthUser0000000000000000000001"));
        api.TimeProvider.Advance(TimeSpan.FromHours(25));

        await DeleteMeAsync(api, TestTokens.Create("AuthUser0000000000000000000002"));

        var deletedUser = Assert.Single(await ReadDeletedUsersAsync(api));
        Assert.Equal("AuthUser0000000000000000000002", deletedUser.AuthUserId);
    }

    private static Task<HttpResponseMessage> SendGetMeAsync(ApiHost api, string token) => SendAsync(api, HttpMethod.Get, token);

    private static async Task<HttpStatusCode> DeleteMeAsync(ApiHost api, string token)
    {
        using var response = await SendAsync(api, HttpMethod.Delete, token);
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> SendAsync(ApiHost api, HttpMethod method, string token)
    {
        using var request = new HttpRequestMessage(method, "/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<User[]> ReadUsersAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.Users.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken));

    private static Task<DeletedUser[]> ReadDeletedUsersAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.DeletedUsers.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken));
}
