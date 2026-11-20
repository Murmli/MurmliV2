using System.Net;
using System.Net.Http.Headers;
using Murmli.Backend.Api.Tests.Infrastructure;

namespace Murmli.Backend.Api.Tests.Extensions;

public class HostApplicationBuilderExtensionsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    [Fact]
    public async Task AddApi_ExplicitAuthorizationWithToken_ReturnsOk()
    {
        await using var api = await ApiHost.StartAsync(connectionString, mapExplicitlyAuthorizedEndpoint: true);

        using var response = await SendAsync(api, ApiHost.ExplicitlyAuthorizedEndpointPath, TestTokens.Create("100000000000000001"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(ApiHost.ExplicitlyAuthorizedEndpointPath)]
    [InlineData("/v1/users/me")]
    public async Task AddApi_TokenWithoutSubject_ReturnsForbidden(string path)
    {
        await using var api = await ApiHost.StartAsync(connectionString, mapExplicitlyAuthorizedEndpoint: true);

        using var response = await SendAsync(api, path, TestTokens.CreateWithoutSubject());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(ApiHost api, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
