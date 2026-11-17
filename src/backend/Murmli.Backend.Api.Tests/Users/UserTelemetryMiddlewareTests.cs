using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Murmli.Backend.Api.Tests.Infrastructure;
using OpenTelemetry.Logs;

namespace Murmli.Backend.Api.Tests.Users;

public class UserTelemetryMiddlewareTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string AuthUserId = "AuthUser0000000000000000000001";

    private const string OtherAuthUserId = "AuthUser0000000000000000000002";

    private const string UserIdProperty = "UserId";

    private const string ProductSearchPath = "/v1/products?search=kefir";

    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    private readonly List<Activity> activities = [];

    private readonly List<LogRecord> logRecords = [];

    [Fact]
    public async Task InvokeAsync_UserWithAccount_AddsUserIdToRequestSpanAndLogs()
    {
        Guid userId;
        ActivityTraceId traceId;
        await using (var api = await StartApiAsync())
        {
            userId = await api.GetMeAsync(AuthUserId);
            traceId = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
        }

        Assert.Equal(userId.ToString(), GetRequestActivity(traceId).GetTagItem(UserIdProperty));
        Assert.Contains(
            GetLogRecords(traceId),
            logRecord => logRecord.CategoryName == "Microsoft.EntityFrameworkCore.Database.Command" && Equals(FindUserId(logRecord), userId)
        );
    }

    [Fact]
    public async Task InvokeAsync_AuthServiceUnreachableDuringDeletion_AddsUserIdToErrorLog()
    {
        Guid userId;
        await using (var api = await StartApiAsync())
        {
            userId = await api.GetMeAsync(AuthUserId);
            api.AuthService.TransportFailure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (auth.test:80)");
            await SendAsync(api, HttpMethod.Delete, "/v1/users/me", TestTokens.Create(AuthUserId), HttpStatusCode.BadGateway);
        }

        var failureLog = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == "AuthUserDeletionFailed");
        Assert.IsType<HttpRequestException>(failureLog.Exception);
        Assert.Equal(userId, FindUserId(failureLog));
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_LogsExceptionWithUserId()
    {
        Guid userId;
        ActivityTraceId traceId;
        await using (var api = await StartApiAsync(mapFailingEndpoint: true))
        {
            userId = await api.GetMeAsync(AuthUserId);
            traceId = await SendAsync(api, HttpMethod.Get, ApiHost.FailingEndpointPath, TestTokens.Create(AuthUserId), HttpStatusCode.InternalServerError);
        }

        var exceptionLog = Assert.Single(
            GetLogRecords(traceId),
            logRecord => logRecord.CategoryName == "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"
        );
        Assert.Equal("UnhandledException", exceptionLog.EventId.Name);
        Assert.Equal(ApiHost.FailingEndpointMessage, Assert.IsType<InvalidOperationException>(exceptionLog.Exception).Message);
        Assert.Equal(userId, FindUserId(exceptionLog));
    }

    [Theory]
    [InlineData("/openapi/v1.json", HttpStatusCode.OK)]
    [InlineData("/v1/users/me", HttpStatusCode.Unauthorized)]
    public async Task InvokeAsync_AnonymousRequest_AddsNoUserId(string path, HttpStatusCode expectedStatusCode)
    {
        ActivityTraceId traceId;
        await using (var api = await StartApiAsync(new Dictionary<string, string?> { ["OpenApi:Enabled"] = "true" }))
        {
            await api.GetMeAsync(AuthUserId);
            traceId = await SendAsync(api, HttpMethod.Get, path, null, expectedStatusCode);
        }

        Assert.Null(GetRequestActivity(traceId).GetTagItem(UserIdProperty));
        Assert.All(GetLogRecords(traceId), logRecord => Assert.Null(FindUserId(logRecord)));
    }

    [Fact]
    public async Task InvokeAsync_UserWithoutAccount_AddsNoUserId()
    {
        ActivityTraceId traceId;
        await using (var api = await StartApiAsync())
        {
            await api.GetMeAsync(OtherAuthUserId);
            traceId = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
        }

        Assert.Null(GetRequestActivity(traceId).GetTagItem(UserIdProperty));
        Assert.All(GetLogRecords(traceId), logRecord => Assert.Null(FindUserId(logRecord)));
    }

    [Fact]
    public async Task InvokeAsync_CreateUseAndDeleteAccount_RecordsAuthUserIdNowhere()
    {
        ActivityTraceId deletionTraceId;
        await using (var api = await StartApiAsync(new Dictionary<string, string?> { ["Telemetry:RecordSensitiveData"] = "false" }))
        {
            await api.GetMeAsync(AuthUserId);
            await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
            deletionTraceId = await SendAsync(api, HttpMethod.Delete, "/v1/users/me", TestTokens.Create(AuthUserId), HttpStatusCode.NoContent);
        }

        GetRequestActivity(deletionTraceId);
        Assert.Contains(logRecords, logRecord => logRecord.EventId.Name == "UserDeleted");
        Assert.All(activities, activity => Assert.DoesNotContain(AuthUserId, Describe(activity)));
        Assert.All(logRecords, logRecord => Assert.DoesNotContain(AuthUserId, Describe(logRecord)));
    }

    private Task<ApiHost> StartApiAsync(IReadOnlyDictionary<string, string?>? additionalConfiguration = null, bool mapFailingEndpoint = false) =>
        ApiHost.StartAsync(connectionString, additionalConfiguration, mapFailingEndpoint, activities, logRecords);

    private static async Task<ActivityTraceId> SendAsync(
        ApiHost api,
        HttpMethod method,
        string path,
        string? token,
        HttpStatusCode expectedStatusCode
    )
    {
        // Span listeners are process-wide, the own trace ID separates this request from those of hosts in parallel tests
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(expectedStatusCode, response.StatusCode);
        return traceId;
    }

    private Activity GetRequestActivity(ActivityTraceId traceId) =>
        Assert.Single(activities, activity => activity.Source.Name == "Microsoft.AspNetCore" && activity.TraceId == traceId);

    private IEnumerable<LogRecord> GetLogRecords(ActivityTraceId traceId) => logRecords.Where(logRecord => logRecord.TraceId == traceId);

    private static List<KeyValuePair<string, object?>> GetScopeItems(LogRecord logRecord)
    {
        var items = new List<KeyValuePair<string, object?>>();
        logRecord.ForEachScope(
            static (scope, scopeItems) =>
            {
                foreach (var item in scope)
                {
                    scopeItems.Add(item);
                }
            },
            items
        );
        return items;
    }

    private static object? FindUserId(LogRecord logRecord) => GetScopeItems(logRecord).SingleOrDefault(item => item.Key == UserIdProperty).Value;

    private static string Describe(Activity activity) =>
        string.Join(
            '\n',
            [
                activity.DisplayName,
                activity.StatusDescription,
                .. activity.TagObjects.Select(Describe),
                .. activity.Events.SelectMany(activityEvent => activityEvent.Tags).Select(Describe),
            ]
        );

    private static string Describe(LogRecord logRecord) =>
        string.Join(
            '\n',
            [
                logRecord.FormattedMessage,
                .. (logRecord.Attributes ?? []).Select(Describe),
                .. GetScopeItems(logRecord).Select(Describe),
                logRecord.Exception?.ToString(),
            ]
        );

    private static string Describe(KeyValuePair<string, object?> item) =>
        $"{item.Key}={Convert.ToString(item.Value, CultureInfo.InvariantCulture)}";
}
