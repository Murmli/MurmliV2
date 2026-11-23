using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Tests.Infrastructure;
using Npgsql;
using OpenTelemetry.Logs;

namespace Murmli.Backend.Api.Tests.Users;

public class UserTelemetryMiddlewareTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string AuthUserId = "AuthUser0000000000000000000001";

    private const string OtherAuthUserId = "AuthUser0000000000000000000002";

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

        Assert.Equal(userId.ToString(), GetRequestActivity(traceId).GetTagItem(TestTelemetry.UserIdProperty));
        Assert.Contains(
            GetLogRecords(traceId),
            logRecord =>
                logRecord.CategoryName == "Microsoft.EntityFrameworkCore.Database.Command" && Equals(TestTelemetry.FindUserId(logRecord), userId)
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
        Assert.Equal(userId, TestTelemetry.FindUserId(failureLog));
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
        Assert.Equal(userId, TestTelemetry.FindUserId(exceptionLog));
    }

    [Fact]
    public async Task InvokeAsync_DatabaseUnavailable_ReturnsServiceUnavailable()
    {
        var traceId = ActivityTraceId.CreateRandom();
        await using (var api = await StartApiAsync())
        {
            await api.GetMeAsync(AuthUserId);
            try
            {
                await SetConnectionsAllowedAsync(false);
                using var response = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), traceId);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.InRange(Assert.NotNull(response.Headers.RetryAfter?.Delta), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20));
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
                Assert.Equal(TestProblemTypes.ServiceUnavailable, problem.GetProperty("type").GetString());
                Assert.Equal(503, problem.GetProperty("status").GetInt32());
            }
            finally
            {
                await SetConnectionsAllowedAsync(true);
            }
        }

        var unavailableLog = Assert.Single(GetLogRecords(traceId), logRecord => logRecord.EventId.Name == "DatabaseUnavailable");
        Assert.Equal(LogLevel.Warning, unavailableLog.LogLevel);
        Assert.NotNull(unavailableLog.Exception);
        Assert.Null(TestTelemetry.FindUserId(unavailableLog));
        Assert.Equal(2, GetLogRecords(traceId).Count(logRecord => logRecord.EventId.Name == CoreEventId.ExecutionStrategyRetrying.Name));
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

        Assert.Null(GetRequestActivity(traceId).GetTagItem(TestTelemetry.UserIdProperty));
        Assert.All(GetLogRecords(traceId), logRecord => Assert.Null(TestTelemetry.FindUserId(logRecord)));
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

        Assert.Null(GetRequestActivity(traceId).GetTagItem(TestTelemetry.UserIdProperty));
        Assert.All(GetLogRecords(traceId), logRecord => Assert.Null(TestTelemetry.FindUserId(logRecord)));
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

    [Fact]
    public async Task InvokeAsync_RequestsOnSameDay_WritesLastRequestOnce()
    {
        DateOnly? lastRequestOnAfterCreation;
        DateOnly? lastRequestOnAfterNextDay;
        ActivityTraceId[] traceIds;
        await using (var api = await StartApiAsync())
        {
            await api.GetMeAsync(AuthUserId);
            lastRequestOnAfterCreation = await ReadLastRequestOnAsync(api);
            var sameDayTraceId = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
            api.TimeProvider.Advance(TimeSpan.FromDays(1));
            var nextDayTraceId = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
            var nextDayAgainTraceId = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
            lastRequestOnAfterNextDay = await ReadLastRequestOnAsync(api);
            traceIds = [sameDayTraceId, nextDayTraceId, nextDayAgainTraceId];
        }

        Assert.Equal(new DateOnly(2026, 1, 1), lastRequestOnAfterCreation);
        Assert.Equal(new DateOnly(2026, 1, 2), lastRequestOnAfterNextDay);
        Assert.Equal([0, 1, 0], traceIds.Select(CountLastRequestUpdates));
    }

    [Fact]
    public async Task InvokeAsync_AccountWithoutLastRequest_WritesTodayOnce()
    {
        DateOnly? lastRequestOn;
        ActivityTraceId[] traceIds;
        await using (var api = await StartApiAsync())
        {
            var userId = await api.GetMeAsync(AuthUserId);
            await new DatabaseAdministration(connectionString).ClearLastRequestAsync(userId);
            var firstTraceId = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
            var secondTraceId = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
            lastRequestOn = await ReadLastRequestOnAsync(api);
            traceIds = [firstTraceId, secondTraceId];
        }

        Assert.Equal(new DateOnly(2026, 1, 1), lastRequestOn);
        Assert.Equal([1, 0], traceIds.Select(CountLastRequestUpdates));
    }

    [Fact]
    public async Task InvokeAsync_LastRequestWrittenMeanwhile_DoesNotOverwrite()
    {
        await using var api = await StartApiAsync();
        var userId = await api.GetMeAsync(AuthUserId);
        api.TimeProvider.Advance(TimeSpan.FromDays(1));
        var administration = new DatabaseAdministration(connectionString);
        Task<ActivityTraceId> request;
        await using (var update = await administration.BeginLastRequestUpdateAsync(userId, new DateOnly(2026, 1, 3)))
        {
            request = SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), HttpStatusCode.OK);
            await administration.WaitForLockWaitersAsync(1);
            await update.CommitAsync();
        }

        await request;

        Assert.Equal(new DateOnly(2026, 1, 3), await ReadLastRequestOnAsync(api));
    }

    [Fact]
    public async Task InvokeAsync_DatabaseUnavailableDuringLastRequestUpdate_ReturnsServiceUnavailable()
    {
        var traceId = ActivityTraceId.CreateRandom();
        Guid userId;
        await using (var setup = await ApiHost.StartAsync(connectionString))
        {
            userId = await setup.GetMeAsync(AuthUserId);
        }

        await using (
            var api = await ApiHost.StartAsync(
                connectionString + ";Command Timeout=1",
                new Dictionary<string, string?> { ["Database:MaxRetryDelay"] = "00:00:00.100" },
                activities: activities,
                logRecords: logRecords
            )
        )
        {
            api.TimeProvider.Advance(TimeSpan.FromDays(1));
            await using (await new DatabaseAdministration(connectionString).BeginLastRequestUpdateAsync(userId, new DateOnly(2026, 1, 2)))
            {
                using var response = await SendAsync(api, HttpMethod.Get, ProductSearchPath, TestTokens.Create(AuthUserId), traceId);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
                Assert.Equal(TestProblemTypes.ServiceUnavailable, problem.GetProperty("type").GetString());
            }
        }

        var unavailableLog = Assert.Single(GetLogRecords(traceId), logRecord => logRecord.EventId.Name == "DatabaseUnavailable");
        Assert.IsType<RetryLimitExceededException>(unavailableLog.Exception);
        Assert.Equal(3, CountLastRequestUpdates(traceId));
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
        using var response = await SendAsync(api, method, path, token, traceId);
        Assert.Equal(expectedStatusCode, response.StatusCode);
        return traceId;
    }

    private static async Task<HttpResponseMessage> SendAsync(ApiHost api, HttpMethod method, string path, string? token, ActivityTraceId traceId)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("traceparent", TestTelemetry.CreateTraceParent(traceId));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task SetConnectionsAllowedAsync(bool allowed)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        Assert.NotNull(database);
        await using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false }.ConnectionString
        );
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var alterCommand = new NpgsqlCommand(
            $"""ALTER DATABASE "{database}" WITH ALLOW_CONNECTIONS {(allowed ? "true" : "false")}""",
            connection
        );
        await alterCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        if (!allowed)
        {
            await using var terminateCommand = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()",
                connection
            );
            terminateCommand.Parameters.AddWithValue("database", database);
            await terminateCommand.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    private static Task<DateOnly?> ReadLastRequestOnAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context =>
            context
                .Users.Where(user => user.AuthUserId == AuthUserId)
                .Select(user => user.LastRequestOn)
                .SingleAsync(TestContext.Current.CancellationToken)
        );

    private int CountLastRequestUpdates(ActivityTraceId traceId) =>
        activities.Count(activity =>
            activity.Source.Name == "Npgsql"
            && activity.TraceId == traceId
            && activity.GetTagItem("db.query.text") is string queryText
            && queryText.StartsWith("UPDATE identity.users", StringComparison.Ordinal)
            && queryText.Contains("last_request_on", StringComparison.Ordinal)
        );

    private Activity GetRequestActivity(ActivityTraceId traceId) =>
        Assert.Single(activities, activity => activity.Source.Name == "Microsoft.AspNetCore" && activity.TraceId == traceId);

    private IEnumerable<LogRecord> GetLogRecords(ActivityTraceId traceId) => logRecords.Where(logRecord => logRecord.TraceId == traceId);

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
                .. TestTelemetry.GetScopeItems(logRecord).Select(Describe),
                logRecord.Exception?.ToString(),
            ]
        );

    private static string Describe(KeyValuePair<string, object?> item) =>
        $"{item.Key}={Convert.ToString(item.Value, CultureInfo.InvariantCulture)}";
}
