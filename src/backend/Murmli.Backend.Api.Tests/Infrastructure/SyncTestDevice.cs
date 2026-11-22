using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class SyncTestDevice
{
    private const string DeviceIdHeader = "Murmli-Device-Id";

    private const string ResponseContentType = "application/json; charset=utf-8";

    private static readonly JsonSerializerOptions ResponseSerializerOptions = new(JsonSerializerOptions.Strict)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ApiHost api;

    private Guid? personalScopeId;

    private SyncTestDevice(ApiHost api, string authUserId, Guid accountId, Guid? personalScopeId)
    {
        this.api = api;
        AuthUserId = authUserId;
        AccountId = accountId;
        this.personalScopeId = personalScopeId;
    }

    public string AuthUserId { get; }

    public Guid AccountId { get; }

    public Guid DeviceId { get; } = Guid.NewGuid();

    public string? Token { get; set; }

    public Guid PersonalScopeId =>
        personalScopeId ?? throw new InvalidOperationException("The device learns its personal scope only from a sync response.");

    public static string CreateAuthUserId() => $"sync-{Guid.NewGuid():N}";

    public static async Task<SyncTestDevice> SignInAsync(ApiHost api, string? authUserId = null, bool firstSync = true)
    {
        var subject = authUserId ?? CreateAuthUserId();
        var device = new SyncTestDevice(api, subject, await api.GetMeAsync(subject), personalScopeId: null);
        if (firstSync)
        {
            await device.SyncAsync();
        }

        return device;
    }

    public static SyncTestDevice WithoutAccount(ApiHost api) => new(api, CreateAuthUserId(), Guid.Empty, personalScopeId: null);

    public static async Task<SyncResponse> ReadResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString();
        Assert.True(
            response.StatusCode == HttpStatusCode.OK && contentType == ResponseContentType,
            $"The sync answered {(int)response.StatusCode} with the Content-Type {contentType}: {body}"
        );
        return Assert.IsType<SyncResponse>(JsonSerializer.Deserialize<SyncResponse>(body, ResponseSerializerOptions));
    }

    public static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string problemType)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(problemType, problem.GetProperty("type").GetString());
        return problem;
    }

    public SyncTestDevice OtherDevice() => new(api, AuthUserId, AccountId, personalScopeId);

    public Task<SyncResponse> SyncAsync(params JsonObject[] commands) => SyncAsync([TestNoteType.DataTypeName], commands);

    public async Task<SyncResponse> SyncAsync(IReadOnlyList<string> dataTypes, IReadOnlyList<JsonObject> commands)
    {
        using var response = await SendAsync(JsonContent.Create(CreateBody(dataTypes, commands)), DeviceId.ToString());
        return await ReadAsync(response);
    }

    public async Task<SyncResponse> SyncRawAsync(string commandsJson)
    {
        var body = $$"""{"token":{{JsonSerializer.Serialize(Token)}},"dataTypes":["{{TestNoteType.DataTypeName}}"],"commands":{{commandsJson}}}""";
        using var response = await SendAsync(new StringContent(body, Encoding.UTF8, "application/json"), DeviceId.ToString());
        return await ReadAsync(response);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpContent? content, string? deviceIdHeader, string? traceParent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/sync") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(AuthUserId));
        if (deviceIdHeader is not null)
        {
            request.Headers.TryAddWithoutValidation(DeviceIdHeader, deviceIdHeader);
        }

        if (traceParent is not null)
        {
            request.Headers.Add("traceparent", traceParent);
        }

        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public JsonObject CreateBody(params JsonObject[] commands) => CreateBody([TestNoteType.DataTypeName], commands);

    public JsonObject CreateNote(
        Guid noteId,
        DateTimeOffset time,
        string title,
        string? color = null,
        decimal? weight = null,
        Guid? scopeId = null
    )
    {
        var fields = new JsonObject { ["title"] = title };
        if (color is not null || weight is not null)
        {
            fields["color"] = color;
            fields["weight"] = weight;
        }

        return CreateCommand("create", noteId, time, scopeId, fields);
    }

    public JsonObject SetNoteFields(Guid noteId, DateTimeOffset time, JsonObject fields, Guid? scopeId = null) =>
        CreateCommand("setFields", noteId, time, scopeId, fields.DeepClone().AsObject());

    public JsonObject DeleteNote(Guid noteId, DateTimeOffset time, Guid? scopeId = null) =>
        CreateCommand("delete", noteId, time, scopeId, fields: null);

    private JsonObject CreateBody(IReadOnlyList<string> dataTypes, IReadOnlyList<JsonObject> commands) =>
        new()
        {
            ["token"] = Token,
            ["dataTypes"] = new JsonArray([.. dataTypes.Select(dataType => JsonValue.Create(dataType))]),
            ["commands"] = new JsonArray([.. commands.Select(command => command.DeepClone())]),
        };

    private JsonObject CreateCommand(string kind, Guid recordId, DateTimeOffset time, Guid? scopeId, JsonObject? fields)
    {
        var command = new JsonObject
        {
            ["id"] = Guid.NewGuid(),
            ["kind"] = kind,
            ["scopeId"] = scopeId ?? PersonalScopeId,
            ["dataType"] = TestNoteType.DataTypeName,
            ["recordId"] = recordId,
            ["time"] = time.ToUnixTimeMilliseconds(),
        };
        if (fields is not null)
        {
            command["fields"] = fields;
        }

        return command;
    }

    private async Task<SyncResponse> ReadAsync(HttpResponseMessage response)
    {
        var syncResponse = await ReadResponseAsync(response);
        Token = syncResponse.Token;
        personalScopeId ??= syncResponse.Scopes.FirstOrDefault(scope => scope.Kind == SyncScope.PersonalKind)?.ScopeId;
        return syncResponse;
    }
}
