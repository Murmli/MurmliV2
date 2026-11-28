using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Murmli.Backend.Api.Tests.Infrastructure;

namespace Murmli.Backend.Api.Tests.Extensions;

public class WebApplicationExtensionsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string DocumentPath = "/openapi/v1.json";

    private static readonly IReadOnlyDictionary<string, string?> OpenApiEnabled = new Dictionary<string, string?>
    {
        ["OpenApi:Enabled"] = "true",
    };

    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    [Fact]
    public async Task UseApi_OpenApiEnabledWithoutToken_ReturnsOk()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var response = await api.HttpClient.GetAsync(DocumentPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UseApi_OpenApiEnabled_DeclaresOpenApi32()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var document = await ReadDocumentAsync(api);

        Assert.Equal("3.2.0", document.RootElement.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task UseApi_OpenApiEnabled_DescribesUserEndpoints()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var document = await ReadDocumentAsync(api);

        var userEndpoints = document.RootElement.GetProperty("paths").GetProperty("/v1/users/me");
        Assert.True(userEndpoints.TryGetProperty("get", out _));
        Assert.True(userEndpoints.TryGetProperty("delete", out _));
    }

    [Fact]
    public async Task UseApi_OpenApiEnabled_DescribesOnlyVersionedPaths()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var document = await ReadDocumentAsync(api);

        var pathNames = document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToArray();
        Assert.NotEmpty(pathNames);
        Assert.All(pathNames, pathName => Assert.StartsWith("/v1/", pathName));
    }

    [Fact]
    public async Task UseApi_OpenApiEnabled_DescribesSyncEndpoint()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var document = await ReadDocumentAsync(api);

        var sync = document.RootElement.GetProperty("paths").GetProperty("/v1/sync").GetProperty("post");
        Assert.Contains("docs/sync.md", sync.GetProperty("description").GetString());
        Assert.Contains(
            sync.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("in").GetString() == "header" && parameter.GetProperty("name").GetString() == "Murmli-Device-Id"
        );
        Assert.True(sync.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.Equal(
            new[] { "200", "400", "410", "413", "415", "429", "503" },
            sync.GetProperty("responses").EnumerateObject().Select(response => response.Name).Order(StringComparer.Ordinal)
        );
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("SyncRequest", out _));
        Assert.True(schemas.TryGetProperty("SyncResponse", out _));
    }

    [Fact]
    public async Task UseApi_OpenApiEnabled_DescribesNumbersWithoutStrings()
    {
        await using var api = await ApiHost.StartAsync(connectionString, OpenApiEnabled);

        using var document = await ReadDocumentAsync(api);

        var serverTime = document
            .RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("SyncResponse")
            .GetProperty("properties")
            .GetProperty("serverTime");
        Assert.Equal("integer", serverTime.GetProperty("type").ToString());
        Assert.False(serverTime.TryGetProperty("pattern", out _));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task UseApi_UnversionedUserPath_ReturnsNotFound(string method)
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await SendWithTokenAsync(api, new HttpMethod(method), "/users/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UseApi_OpenApiDisabledWithToken_ReturnsNotFound()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await SendWithTokenAsync(api, HttpMethod.Get, DocumentPath);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UseApi_OpenApiDisabledWithoutToken_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await api.HttpClient.GetAsync(DocumentPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UseApi_UnhandledException_ReturnsProblemDetails()
    {
        await using var api = await ApiHost.StartAsync(connectionString, mapFailingEndpoint: true);

        using var response = await SendWithTokenAsync(api, HttpMethod.Get, ApiHost.FailingEndpointPath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<ProblemDetails>(problem).Status);
    }

    private static async Task<JsonDocument> ReadDocumentAsync(ApiHost api)
    {
        using var response = await api.HttpClient.GetAsync(DocumentPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return await JsonDocument.ParseAsync(content, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> SendWithTokenAsync(ApiHost api, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create("100000000000000001"));
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
