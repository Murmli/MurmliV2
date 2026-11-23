using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Data.Identity;
using Murmli.Backend.Sync.Cleanup;
using OpenTelemetry.Logs;

namespace Murmli.Backend.Api.Tests.Users;

public class StaleAccountDeletionContributorTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string CompletedEvent = "StaleAccountDeletionCompleted";

    [Fact]
    public async Task RunAsync_AuthDeletionFailsOnce_CompletesDeletionOnNextRun()
    {
        var logRecords = new List<LogRecord>();
        await using var api = await ApiHost.StartAsync(postgres.CreateDatabaseConnectionString(), logRecords: logRecords, testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        await device.ApplyAllAsync(device.CreateNote(Guid.NewGuid(), api.TimeProvider.GetUtcNow(), "note"));
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.BadGateway, await api.DeleteMeAsync(device.AuthUserId));
        api.TimeProvider.Advance(TimeSpan.FromMinutes(11));

        await RunCleanupAsync(api);

        Assert.NotNull(Assert.Single(await ReadUsersAsync(api)).DeletionStartedAt);
        Assert.Equal(2, api.AuthService.Requests.Count);
        Assert.DoesNotContain(logRecords, logRecord => logRecord.EventId.Name == CompletedEvent);
        var runFailure = logRecords.Where(logRecord => logRecord.EventId.Name == "AuthUserDeletionFailed").Skip(1).Single();
        Assert.Equal(device.AccountId, TestTelemetry.FindUserId(runFailure));
        api.AuthService.ResponseStatusCode = HttpStatusCode.NoContent;

        await RunCleanupAsync(api);

        Assert.Empty(await ReadUsersAsync(api));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, device.PersonalScopeId));
        Assert.Equal(3, api.AuthService.Requests.Count);
        var completed = Assert.Single(logRecords, logRecord => logRecord.EventId.Name == CompletedEvent);
        Assert.Equal(LogLevel.Information, completed.LogLevel);
        Assert.Equal(device.AccountId, completed.Attributes!.ToDictionary()[TestTelemetry.UserIdProperty]);
        Assert.Equal(2, logRecords.Count(logRecord => logRecord.EventId.Name == "SyncCleanupCompleted"));
    }

    [Fact]
    public async Task RunAsync_AuthDeletionFailsForFirstAccount_CompletesNextAccount()
    {
        const string failingAuthUserId = "AuthUser0000000000000000000001";
        const string completedAuthUserId = "AuthUser0000000000000000000002";
        await using var api = await ApiHost.StartAsync(postgres.CreateDatabaseConnectionString());
        await api.GetMeAsync(failingAuthUserId);
        await api.GetMeAsync(completedAuthUserId);
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.BadGateway, await api.DeleteMeAsync(failingAuthUserId));
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.BadGateway, await api.DeleteMeAsync(completedAuthUserId));
        api.AuthService.ResponseStatusCode = HttpStatusCode.NoContent;
        api.AuthService.FailingAuthUserId = failingAuthUserId;
        api.TimeProvider.Advance(TimeSpan.FromMinutes(11));

        await RunCleanupAsync(api);

        Assert.Equal([failingAuthUserId], (await ReadUsersAsync(api)).Select(user => user.AuthUserId));
    }

    [Fact]
    public async Task RunAsync_MarkedTenMinutesAgo_CompletesOnlyAfterwards()
    {
        await using var api = await ApiHost.StartAsync(postgres.CreateDatabaseConnectionString());
        const string authUserId = "AuthUser0000000000000000000001";
        await api.GetMeAsync(authUserId);
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.BadGateway, await api.DeleteMeAsync(authUserId));
        api.TimeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.BadGateway, await api.DeleteMeAsync(authUserId));
        api.AuthService.ResponseStatusCode = HttpStatusCode.NoContent;
        api.TimeProvider.Advance(TimeSpan.FromMinutes(5));

        await RunCleanupAsync(api);

        Assert.Single(await ReadUsersAsync(api));
        Assert.Equal(2, api.AuthService.Requests.Count);
        api.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));

        await RunCleanupAsync(api);

        Assert.Empty(await ReadUsersAsync(api));
        Assert.Equal(3, api.AuthService.Requests.Count);
    }

    [Fact]
    public async Task RunAsync_MoreStaleAccountsThanBatchSize_CompletesOldestMarkedFirst()
    {
        await using var api = await ApiHost.StartAsync(
            postgres.CreateDatabaseConnectionString(),
            new Dictionary<string, string?> { ["Sync:Cleanup:BatchSize"] = "1" }
        );
        await api.GetMeAsync("AuthUser0000000000000000000001");
        await api.GetMeAsync("AuthUser0000000000000000000002");
        // Marking the account with the larger id first keeps an order by id alone from passing
        var authUserIdsByDescendingId = await api.QueryDatabaseAsync(context =>
            context.Users.OrderByDescending(user => user.Id).Select(user => user.AuthUserId).ToArrayAsync(TestContext.Current.CancellationToken)
        );
        var olderAuthUserId = authUserIdsByDescendingId[0];
        var newerAuthUserId = authUserIdsByDescendingId[1];
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.BadGateway, await api.DeleteMeAsync(olderAuthUserId));
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.BadGateway, await api.DeleteMeAsync(newerAuthUserId));
        api.TimeProvider.Advance(TimeSpan.FromDays(1));
        // The first request of a new day rewrites the older account's row, so the physical row order differs from the marking order
        Assert.Equal(HttpStatusCode.BadGateway, await api.DeleteMeAsync(olderAuthUserId));
        api.AuthService.ResponseStatusCode = HttpStatusCode.NoContent;

        await RunCleanupAsync(api);

        Assert.Equal([newerAuthUserId], (await ReadUsersAsync(api)).Select(user => user.AuthUserId));

        await RunCleanupAsync(api);

        Assert.Empty(await ReadUsersAsync(api));
    }

    private static Task RunCleanupAsync(ApiHost api) =>
        api.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(TestContext.Current.CancellationToken);

    private static Task<User[]> ReadUsersAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.Users.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken));
}
