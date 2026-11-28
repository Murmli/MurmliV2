using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Sync.Cleanup;
using OpenTelemetry.Metrics;

namespace Murmli.Backend.Api.Tests.Sync;

[Collection<TelemetryCollection>]
public class SyncCleanupServiceTelemetryTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(45);

    private static readonly TimeSpan SafetyMargin = TimeSpan.FromDays(1);

    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    private readonly List<Activity> activities = [];

    private readonly List<MetricSnapshot> metricSnapshots = [];

    [Fact]
    public async Task RunAsync_ExpiredEntries_RecordsSucceededRunWithRemovedRowsAndRootSpan()
    {
        DateTimeOffset runTime;
        await using (
            var api = await ApiHost.StartAsync(
                connectionString,
                activities: activities,
                testDataTypes: true,
                testCleanupContributors: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            var noteId = Guid.NewGuid();
            await device.ApplyAllAsync(device.CreateNote(noteId, now, "note"), device.DeleteNote(noteId, now));
            runTime = now + Retention + SafetyMargin + TimeSpan.FromMinutes(1);
            api.TimeProvider.SetUtcNow(runTime);

            await RunCleanupAsync(api, TestContext.Current.CancellationToken);

            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long> { ["succeeded"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.runs", "murmli.sync.cleanup.result")
        );
        Assert.Equal(new Dictionary<string, long> { [""] = 1 }, TestMetrics.CountByTags(metricSnapshots, "murmli.sync.cleanup.duration"));
        Assert.Equal(
            new Dictionary<string, long>
            {
                ["tombstones"] = 1,
                ["processed_commands"] = 3,
                ["stale_account_deletions"] = 0,
                ["checked_shopping_list_items"] = 0,
                ["test_throwing"] = 0,
                [ConstantCleanupContributor.ContributorName] = ConstantCleanupContributor.RemovedRecordCount,
            },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.removed", "murmli.sync.cleanup.kind")
        );
        Assert.Equal(runTime.ToUnixTimeSeconds(), TestMetrics.GetLastGaugeValue(metricSnapshots, "murmli.sync.cleanup.last_success_time"));
        var run = GetRunActivity();
        Assert.Equal("Murmli.Backend.Sync", run.Source.Name);
        Assert.Equal(ActivityKind.Internal, run.Kind);
        Assert.Equal(default(ActivitySpanId), run.ParentSpanId);
        Assert.Equal(ActivityStatusCode.Unset, run.Status);
        Assert.Contains(activities, activity => activity.Source.Name == "Npgsql" && activity.ParentSpanId == run.SpanId);
    }

    [Fact]
    public async Task RunAsync_LeaseHeld_RecordsSkippedRunOnly()
    {
        await using (
            var api = await ApiHost.StartAsync(connectionString, activities: activities, testDataTypes: true, metricSnapshots: metricSnapshots)
        )
        {
            await new DatabaseAdministration(connectionString).HoldCleanupLeaseAsync(api.TimeProvider.GetUtcNow().AddHours(1));

            await RunCleanupAsync(api, TestContext.Current.CancellationToken);

            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long> { ["skipped"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.runs", "murmli.sync.cleanup.result")
        );
        Assert.Empty(TestMetrics.CountByTags(metricSnapshots, "murmli.sync.cleanup.duration"));
        Assert.Empty(TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.removed", "murmli.sync.cleanup.kind"));
        Assert.Null(TestMetrics.GetLastGaugeValue(metricSnapshots, "murmli.sync.cleanup.last_success_time"));
        Assert.Equal(ActivityStatusCode.Unset, GetRunActivity().Status);
    }

    [Fact]
    public async Task RunAsync_CancelledByCaller_RecordsFailedRunWithErrorSpan()
    {
        await using (
            var api = await ApiHost.StartAsync(connectionString, activities: activities, testDataTypes: true, metricSnapshots: metricSnapshots)
        )
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunCleanupAsync(api, cancellation.Token));

            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long> { ["failed"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.runs", "murmli.sync.cleanup.result")
        );
        Assert.Equal(new Dictionary<string, long> { [""] = 1 }, TestMetrics.CountByTags(metricSnapshots, "murmli.sync.cleanup.duration"));
        Assert.Equal(
            new Dictionary<string, long> { ["tombstones"] = 0, ["processed_commands"] = 0 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.removed", "murmli.sync.cleanup.kind")
        );
        Assert.Null(TestMetrics.GetLastGaugeValue(metricSnapshots, "murmli.sync.cleanup.last_success_time"));
        Assert.Equal(ActivityStatusCode.Error, GetRunActivity().Status);
    }

    [Fact]
    public async Task RunAsync_ContributorDisturbed_RecordsFailedRunWithEarlierRemovals()
    {
        await using (
            var api = await ApiHost.StartAsync(
                connectionString,
                activities: activities,
                testDataTypes: true,
                metricSnapshots: metricSnapshots,
                disturbedCleanupContributor: true
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            var noteId = Guid.NewGuid();
            await device.ApplyAllAsync(device.CreateNote(noteId, now, "note"), device.DeleteNote(noteId, now));
            api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));

            await RunCleanupAsync(api, TestContext.Current.CancellationToken);

            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long> { ["failed"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.runs", "murmli.sync.cleanup.result")
        );
        Assert.Equal(new Dictionary<string, long> { [""] = 1 }, TestMetrics.CountByTags(metricSnapshots, "murmli.sync.cleanup.duration"));
        Assert.Equal(
            new Dictionary<string, long>
            {
                ["tombstones"] = 1,
                ["processed_commands"] = 3,
                ["stale_account_deletions"] = 0,
                ["checked_shopping_list_items"] = 0,
                ["test_disturbed"] = 0,
            },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.removed", "murmli.sync.cleanup.kind")
        );
        Assert.Null(TestMetrics.GetLastGaugeValue(metricSnapshots, "murmli.sync.cleanup.last_success_time"));
        Assert.Equal(ActivityStatusCode.Error, GetRunActivity().Status);
    }

    [Fact]
    public async Task RunAsync_ContributorCancelledAfterRemovals_RecordsItsReportedRemovals()
    {
        using var cleanupCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using (
            var api = await ApiHost.StartAsync(connectionString, metricSnapshots: metricSnapshots, cleanupCancellation: cleanupCancellation)
        )
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunCleanupAsync(api, cleanupCancellation.Token));

            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long> { ["failed"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.runs", "murmli.sync.cleanup.result")
        );
        Assert.Equal(
            new Dictionary<string, long>
            {
                ["tombstones"] = 0,
                ["processed_commands"] = 0,
                ["stale_account_deletions"] = 0,
                ["checked_shopping_list_items"] = 0,
                ["test_cancelling"] = CancellingCleanupContributor.RemovedRecordCount,
            },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.removed", "murmli.sync.cleanup.kind")
        );
    }

    [Fact]
    public async Task RunAsync_StepFails_RecordsFailedRunWithPartialCounts()
    {
        await using (
            var api = await ApiHost.StartAsync(
                connectionString,
                new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
                activities: activities,
                testDataTypes: true,
                metricSnapshots: metricSnapshots
            )
        )
        {
            var device = await SyncTestDevice.SignInAsync(api);
            var now = api.TimeProvider.GetUtcNow();
            var noteId = Guid.NewGuid();
            await device.ApplyAllAsync(device.CreateNote(noteId, now, "note"), device.DeleteNote(noteId, now));
            api.TimeProvider.Advance(Retention + SafetyMargin + TimeSpan.FromMinutes(1));

            await using (await new DatabaseAdministration(connectionString).LockProcessedCommandsTableAsync())
            {
                await RunCleanupAsync(api, TestContext.Current.CancellationToken);
            }

            api.FlushMetrics();
        }

        Assert.Equal(
            new Dictionary<string, long> { ["failed"] = 1 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.runs", "murmli.sync.cleanup.result")
        );
        Assert.Equal(new Dictionary<string, long> { [""] = 1 }, TestMetrics.CountByTags(metricSnapshots, "murmli.sync.cleanup.duration"));
        Assert.InRange(TestMetrics.HistogramSumByTags(metricSnapshots, "murmli.sync.cleanup.duration")[""], 1.0, 60.0);
        Assert.Equal(
            new Dictionary<string, long> { ["tombstones"] = 1, ["processed_commands"] = 0 },
            TestMetrics.SumByTags(metricSnapshots, "murmli.sync.cleanup.removed", "murmli.sync.cleanup.kind")
        );
        Assert.Null(TestMetrics.GetLastGaugeValue(metricSnapshots, "murmli.sync.cleanup.last_success_time"));
        Assert.Equal(ActivityStatusCode.Error, GetRunActivity().Status);
    }

    private static Task RunCleanupAsync(ApiHost api, CancellationToken cancellationToken) =>
        api.Services.GetRequiredService<SyncCleanupRunner>().RunAsync(cancellationToken);

    private Activity GetRunActivity() =>
        Assert.Single(activities, activity => activity.DisplayName == "sync cleanup run");
}
