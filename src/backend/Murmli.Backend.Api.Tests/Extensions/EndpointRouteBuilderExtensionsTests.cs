using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Murmli.Backend.Api.Tests.Infrastructure;

namespace Murmli.Backend.Api.Tests.Extensions;

public class EndpointRouteBuilderExtensionsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string DocumentPath = "/openapi/v1.json";

    private static readonly IReadOnlyDictionary<string, string?> OpenApiEnabled = new Dictionary<string, string?>
    {
        ["OpenApi:Enabled"] = "true",
    };

    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    [Fact]
    public async Task MapApi_OpenApiEnabledWithoutToken_ReturnsOk()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var response = await api.HttpClient.GetAsync(DocumentPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MapApi_OpenApiEnabled_DeclaresOpenApi32()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var document = await ReadDocumentAsync(api);

        Assert.Equal("3.2.0", document.RootElement.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task MapApi_OpenApiEnabled_DescribesUserEndpoints()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var document = await ReadDocumentAsync(api);

        var userEndpoints = document.RootElement.GetProperty("paths").GetProperty("/users/me");
        Assert.True(userEndpoints.TryGetProperty("get", out _));
        Assert.True(userEndpoints.TryGetProperty("delete", out _));
    }

    [Fact]
    public async Task MapApi_OpenApiDisabledWithToken_ReturnsNotFound()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        using var request = new HttpRequestMessage(HttpMethod.Get, DocumentPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create("100000000000000001"));

        using var response = await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MapApi_OpenApiDisabledWithoutToken_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await api.HttpClient.GetAsync(DocumentPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<JsonDocument> ReadDocumentAsync(ApiHost api)
    {
        using var response = await api.HttpClient.GetAsync(DocumentPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return await JsonDocument.ParseAsync(content, cancellationToken: TestContext.Current.CancellationToken);
    }
}
