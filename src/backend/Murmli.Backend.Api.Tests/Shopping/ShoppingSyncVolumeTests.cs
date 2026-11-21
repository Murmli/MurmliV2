using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Murmli.Backend.Api.Shopping;
using Murmli.Backend.Api.Tests.Infrastructure;
using Npgsql;

namespace Murmli.Backend.Api.Tests.Shopping;

public class ShoppingSyncVolumeTests
{
    private const int CommandsPerRequest = 500;

    private const int FirstSyncRepetitions = 3;

    private const int NoOpSyncRepetitions = 20;

    private const int ConcurrentFirstSyncCount = 10;

    private const int CrashLogLineCount = 30;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(600);

    private static readonly TimeSpan IngressTimeout = TimeSpan.FromSeconds(240);

    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);

    [Fact(Explicit = true)]
    public async Task SyncAsync_LargeAccountsAndLongQueues_ReportsDurationsAndSizes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accountSizes = (Environment.GetEnvironmentVariable("SYNC_VOLUME_ACCOUNTS") ?? "5x50,20x200,40x500")
            .Split(',')
            .Select(account => account.Split('x'))
            .Select(parts => new AccountSize(int.Parse(parts[0]), int.Parse(parts[1])))
            .ToArray();
        var queueLengths = (Environment.GetEnvironmentVariable("SYNC_VOLUME_QUEUE_LENGTHS") ?? "100,1000,5000,10000")
            .Split(',')
            .Select(int.Parse)
            .ToArray();
        Report($"Knobs: accounts {string.Join(", ", accountSizes.Select(size => size.Name))}, queue lengths {string.Join(", ", queueLengths)}");

        await using var container = await LimitedApiContainer.StartAsync(cancellationToken: cancellationToken);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = container.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        Report($"Baseline: {Describe(await container.GetResourceUsageAsync(cancellationToken))}");

        var accountSummaries = new List<AccountSummary>();
        var queueSummaries = new List<QueueSummary>();
        ConcurrentSummary? concurrentSummary = null;
        string? containerFailure = null;
        try
        {
            foreach (var (size, index) in accountSizes.Select((size, index) => (size, index)))
            {
                var summary = new AccountSummary(size, $"VolumeUser{index:D4}");
                accountSummaries.Add(summary);
                containerFailure = await MeasureAccountAsync(container, client, summary, cancellationToken);
                if (containerFailure is not null)
                {
                    break;
                }
            }

            containerFailure ??= await MeasureQueuesAsync(container, client, queueLengths, queueSummaries, cancellationToken);
            if (containerFailure is null)
            {
                var largest = accountSummaries.MaxBy(summary => summary.Size.Lists * summary.Size.ItemsPerList)!;
                containerFailure = await RunStepsAsync(
                    container,
                    client,
                    [
                        (
                            $"concurrent first syncs for {largest.Size.Name}",
                            async () => concurrentSummary = await MeasureConcurrentFirstSyncsAsync(container, client, largest, cancellationToken)
                        ),
                    ],
                    cancellationToken
                );
            }
        }
        finally
        {
            ReportSummary(accountSummaries, queueSummaries, concurrentSummary, containerFailure);
        }

        Assert.True(containerFailure is null, containerFailure);
    }

    private static async Task<string?> MeasureAccountAsync(
        LimitedApiContainer container,
        HttpClient client,
        AccountSummary summary,
        CancellationToken cancellationToken
    )
    {
        Report($"--- Account {summary.Size.Name} ---");
        var authUserId = summary.AuthUserId;
        var listIds = Enumerable.Range(0, summary.Size.Lists).Select(_ => Guid.NewGuid()).ToArray();
        var itemIds = listIds.Select(_ => Enumerable.Range(0, summary.Size.ItemsPerList).Select(_ => Guid.NewGuid()).ToArray()).ToArray();
        string? deviceAToken = null;
        string? deviceBToken = null;

        return await RunStepsAsync(
            container,
            client,
            [
                (
                    "seeding",
                    async () =>
                    {
                        await GetMeAsync(client, authUserId, cancellationToken);
                        var requests = new List<SyncCommand[]>
                        {
                            listIds.Select((listId, listIndex) => Command("createList", new { listId, name = $"List {listIndex}" })).ToArray(),
                        };
                        requests.AddRange(
                            listIds
                                .SelectMany(
                                    (listId, listIndex) =>
                                        itemIds[listIndex].Select((itemId, itemIndex) => Command("addItem", new { itemId, listId, name = $"Item {itemIndex}" }))
                                )
                                .Chunk(CommandsPerRequest)
                        );
                        var durations = new List<TimeSpan>();
                        foreach (var commands in requests)
                        {
                            var call = await SyncAsync(client, authUserId, deviceAToken, commands, cancellationToken);
                            AssertAllApplied(call);
                            deviceAToken = call.Response.Token;
                            durations.Add(call.Duration);
                        }

                        var total = durations.Aggregate(TimeSpan.Zero, (sum, duration) => sum + duration);
                        var commandCount = requests.Sum(commands => commands.Length);
                        summary.Seed = new SeedSummary(total, Percentile(durations, 0.5)!.Value, Percentile(durations, 1)!.Value, commandCount);
                        Report(
                            $"Seeding: {commandCount} commands in {requests.Count} requests, total {Describe(total)}, "
                                + $"median per request {Describe(summary.Seed.Median)}, max {Describe(summary.Seed.Maximum)}, "
                                + $"{total.TotalMilliseconds / commandCount:F2} ms per command"
                        );
                    }
                ),
                (
                    "first sync",
                    async () =>
                    {
                        var before = await container.GetResourceUsageAsync(cancellationToken);
                        var calls = new List<SyncCall>();
                        for (var repetition = 0; repetition < FirstSyncRepetitions; repetition++)
                        {
                            calls.Add(await SyncAsync(client, authUserId, null, [], cancellationToken));
                        }

                        var after = await container.GetResourceUsageAsync(cancellationToken);
                        var last = calls[^1];
                        deviceBToken = last.Response.Token;
                        summary.FirstSync = new FirstSyncSummary(
                            Percentile(calls.Select(call => call.Duration), 0.5)!.Value,
                            last.ResponseBody.Length,
                            GzipLength(last.ResponseBody),
                            last.Response.Lists.Count,
                            last.Response.Items.Count,
                            after.MemoryCurrentBytes
                        );
                        Report(
                            $"First sync ({FirstSyncRepetitions}x): median {Describe(summary.FirstSync.Median)}, "
                                + $"{summary.FirstSync.Bytes} bytes, gzip {summary.FirstSync.GzipBytes} bytes, "
                                + $"{summary.FirstSync.Lists} lists, {summary.FirstSync.Items} items, "
                                + $"{(double)summary.FirstSync.Bytes / Math.Max(1, summary.FirstSync.Items):F0} bytes per item"
                        );
                        Report($"  before: {Describe(before)}");
                        Report($"  after:  {Describe(after)}");
                    }
                ),
                (
                    "no-op sync",
                    async () =>
                    {
                        var calls = new List<SyncCall>();
                        for (var repetition = 0; repetition < NoOpSyncRepetitions; repetition++)
                        {
                            var call = await SyncAsync(client, authUserId, deviceBToken, [], cancellationToken);
                            deviceBToken = call.Response.Token;
                            calls.Add(call);
                        }

                        summary.NoOp = new NoOpSummary(
                            Percentile(calls.Select(call => call.Duration), 0.5)!.Value,
                            Percentile(calls.Select(call => call.Duration), 0.95)!.Value,
                            calls[^1].ResponseBody.Length,
                            deviceBToken!.Length
                        );
                        Report(
                            $"No-op sync ({NoOpSyncRepetitions}x): median {Describe(summary.NoOp.Median)}, "
                                + $"95th percentile {Describe(summary.NoOp.Percentile95)}, {summary.NoOp.Bytes} bytes, "
                                + $"token {summary.NoOp.TokenLength} characters"
                        );
                    }
                ),
                (
                    "small delta",
                    async () =>
                    {
                        var rename = await SyncAsync(
                            client,
                            authUserId,
                            deviceAToken,
                            [Command("renameItem", new { itemId = itemIds[^1][0], name = "Renamed item" })],
                            cancellationToken
                        );
                        AssertAllApplied(rename);
                        deviceAToken = rename.Response.Token;
                        var call = await SyncAsync(client, authUserId, deviceBToken, [], cancellationToken);
                        deviceBToken = call.Response.Token;
                        summary.Delta = new DeltaSummary(call.Duration, call.ResponseBody.Length, call.Response.Items.Count);
                        Report(
                            $"Small delta: device B synced in {Describe(call.Duration)}, {call.ResponseBody.Length} bytes, "
                                + $"{call.Response.Items.Count} items"
                        );
                    }
                ),
                (
                    "deletions",
                    async () =>
                    {
                        var deletedListCount = listIds.Length / 2;
                        var deletionRequests = itemIds[..deletedListCount]
                            .SelectMany(ids => ids)
                            .Select(itemId => Command("deleteItem", new { itemId }))
                            .Chunk(CommandsPerRequest)
                            .ToArray();
                        var deletionDuration = TimeSpan.Zero;
                        foreach (var commands in deletionRequests)
                        {
                            var deletion = await SyncAsync(client, authUserId, deviceAToken, commands, cancellationToken);
                            AssertAllApplied(deletion);
                            deviceAToken = deletion.Response.Token;
                            deletionDuration += deletion.Duration;
                        }

                        var returning = await SyncAsync(client, authUserId, deviceBToken, [], cancellationToken);
                        deviceBToken = returning.Response.Token;
                        var fresh = await SyncAsync(client, authUserId, null, [], cancellationToken);
                        summary.Deletion = new DeletionSummary(
                            returning.Duration,
                            returning.ResponseBody.Length,
                            returning.Response.Items.Count(item => item.Deleted),
                            fresh.Duration,
                            fresh.ResponseBody.Length,
                            fresh.Response.Items.Count,
                            fresh.Response.Items.Count(item => item.Deleted)
                        );
                        Report(
                            $"Deletions: device A deleted the items of {deletedListCount} lists "
                                + $"({deletionRequests.Sum(commands => commands.Length)} commands) in {Describe(deletionDuration)}"
                        );
                        Report(
                            $"  returning device B: {Describe(returning.Duration)}, {returning.ResponseBody.Length} bytes, "
                                + $"{summary.Deletion.ReturningDeleted} entries with deleted: true"
                        );
                        Report(
                            $"  fresh device C: {Describe(fresh.Duration)}, {fresh.ResponseBody.Length} bytes, "
                                + $"{summary.Deletion.FreshItems} items, {summary.Deletion.FreshDeleted} with deleted: true"
                        );
                    }
                ),
                (
                    "database sizes",
                    async () =>
                    {
                        summary.Database = await GetDatabaseSizesAsync(container, cancellationToken);
                        Report(
                            $"Database (all users so far): processed_commands {summary.Database.ProcessedCommands} rows, "
                                + $"{Mebibytes(summary.Database.ProcessedCommandsBytes)}, "
                                + $"{(double)summary.Database.ProcessedCommandsBytes / summary.Database.ProcessedCommands:F0} bytes per row; "
                                + $"items {summary.Database.Items} rows ({summary.Database.DeletedItems} deleted markers), "
                                + $"{Mebibytes(summary.Database.ItemsBytes)}, {(double)summary.Database.ItemsBytes / summary.Database.Items:F0} bytes per row"
                        );
                    }
                ),
            ],
            cancellationToken
        );
    }

    private static async Task<string?> MeasureQueuesAsync(
        LimitedApiContainer container,
        HttpClient client,
        int[] queueLengths,
        List<QueueSummary> summaries,
        CancellationToken cancellationToken
    )
    {
        Report("--- Queue lengths ---");
        const string authUserId = "VolumeQueueUser";
        var listId = Guid.NewGuid();
        string? token = null;
        var setup = (
            "queue setup",
            (Func<Task>)(
                async () =>
                {
                    await GetMeAsync(client, authUserId, cancellationToken);
                    var call = await SyncAsync(client, authUserId, null, [Command("createList", new { listId, name = "Queue" })], cancellationToken);
                    AssertAllApplied(call);
                    token = call.Response.Token;
                }
            )
        );
        var lengthSteps = queueLengths.Select(length =>
            (
                $"queue of {length}",
                (Func<Task>)(
                    async () =>
                    {
                        var commands = Enumerable
                            .Range(0, length)
                            .Select(index => Command("addItem", new { itemId = Guid.NewGuid(), listId, name = $"Queued item {index}" }))
                            .ToArray();
                        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(new SyncRequest(token, commands), JsonSerializerOptions.Web).Length;
                        var before = await container.GetResourceUsageAsync(cancellationToken);
                        var start = Stopwatch.GetTimestamp();
                        string status;
                        int? applied = null;
                        try
                        {
                            var call = await SyncAsync(client, authUserId, token, commands, cancellationToken);
                            token = call.Response.Token;
                            status = "200";
                            applied = call.Response.Results.Count(result => result.Status == "applied");
                        }
                        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                        {
                            status = DescribeFailure(exception);
                        }

                        var duration = Stopwatch.GetElapsedTime(start);
                        var after = await container.GetResourceUsageAsync(cancellationToken);
                        var summary = new QueueSummary(length, requestBytes, status, duration, applied, before.MemoryCurrentBytes, after.MemoryCurrentBytes);
                        summaries.Add(summary);
                        Report(
                            $"Queue of {length}: request {requestBytes} bytes, {status}, {Describe(duration)}"
                                + $"{(duration > IngressTimeout ? " (ABOVE the 240 s ingress timeout)" : "")}, "
                                + $"{duration.TotalMilliseconds / length:F2} ms per command, applied {applied?.ToString() ?? "-"}"
                        );
                        Report($"  before: {Describe(before)}");
                        Report($"  after:  {Describe(after)}");
                    }
                )
            )
        );
        return await RunStepsAsync(container, client, [setup, .. lengthSteps], cancellationToken);
    }

    private static async Task<ConcurrentSummary> MeasureConcurrentFirstSyncsAsync(
        LimitedApiContainer container,
        HttpClient client,
        AccountSummary account,
        CancellationToken cancellationToken
    )
    {
        Report($"--- {ConcurrentFirstSyncCount} concurrent first syncs for {account.Size.Name} ---");
        var durations = new ConcurrentBag<TimeSpan>();
        var failures = new ConcurrentDictionary<string, int>();
        await Task.WhenAll(
            Enumerable
                .Range(0, ConcurrentFirstSyncCount)
                .Select(_ =>
                    Task.Run(
                        async () =>
                        {
                            try
                            {
                                durations.Add((await SyncAsync(client, account.AuthUserId, null, [], cancellationToken)).Duration);
                            }
                            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                            {
                                failures.AddOrUpdate(DescribeFailure(exception), 1, (_, count) => count + 1);
                            }
                        },
                        cancellationToken
                    )
                )
        );
        var after = await container.GetResourceUsageAsync(cancellationToken);
        var summary = new ConcurrentSummary(
            account.Size.Name,
            Percentile(durations, 0.5),
            Percentile(durations, 1),
            failures.IsEmpty ? "none" : string.Join(", ", failures.Select(failure => $"{failure.Key} {failure.Value}")),
            after.MemoryPeakBytes
        );
        Report(
            $"Concurrent first syncs: median {Describe(summary.Median)}, max {Describe(summary.Maximum)}, failures: {summary.Failures}"
        );
        Report($"  after: {Describe(after)}");
        return summary;
    }

    private static async Task<DatabaseSizes> GetDatabaseSizesAsync(LimitedApiContainer container, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(container.DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM shopping.processed_commands),
                pg_total_relation_size('shopping.processed_commands'),
                (SELECT count(*) FROM shopping.items),
                (SELECT count(*) FROM shopping.items WHERE deleted_at IS NOT NULL),
                pg_total_relation_size('shopping.items')
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DatabaseSizes(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
    }

    private static async Task<string?> RunStepsAsync(
        LimitedApiContainer container,
        HttpClient client,
        (string Name, Func<Task> Run)[] steps,
        CancellationToken cancellationToken
    )
    {
        foreach (var (name, run) in steps)
        {
            ExceptionDispatchInfo? stepFailure = null;
            try
            {
                await run();
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                stepFailure = ExceptionDispatchInfo.Capture(exception);
            }

            if (await FindContainerFailureAsync(container, client, cancellationToken) is { } containerFailure)
            {
                return $"The API container did not survive the step '{name}': {containerFailure}";
            }

            stepFailure?.Throw();
        }

        return null;
    }

    private static async Task<string?> FindContainerFailureAsync(
        LimitedApiContainer container,
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < HealthTimeout)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(HealthTimeout);
                using var response = await client.GetAsync("/health", timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    return null;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        string state;
        using (var exitTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            exitTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var exitCode = await container.GetApiExitCodeAsync(exitTimeout.Token);
                state = $"the container exited with code {exitCode}{(exitCode == 137 ? " (SIGKILL, typical for an OOM kill)" : "")}";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                state = "the container is still running";
            }
        }

        var (apiLog, _) = await container.GetLogsAsync(default, cancellationToken);
        Report($"Last {CrashLogLineCount} lines of the API log:");
        foreach (var line in apiLog.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(CrashLogLineCount))
        {
            Report($"  {line}");
        }

        return $"it stopped answering /health for {HealthTimeout.TotalSeconds:F0} s, {state}";
    }

    private static async Task GetMeAsync(HttpClient client, string authUserId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "/v1/users/me", authUserId);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<SyncCall> SyncAsync(
        HttpClient client,
        string authUserId,
        string? token,
        SyncCommand[] commands,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = CreateRequest(HttpMethod.Post, "/v1/sync", authUserId);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new SyncRequest(token, commands), JsonSerializerOptions.Web))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        };
        var start = Stopwatch.GetTimestamp();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        var duration = Stopwatch.GetElapsedTime(start);
        return new SyncCall(duration, responseBody, JsonSerializer.Deserialize<SyncResponse>(responseBody, JsonSerializerOptions.Web)!);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string authUserId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(authUserId));
        return request;
    }

    private static SyncCommand Command(string type, object arguments) =>
        new(Guid.NewGuid(), type, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(arguments, JsonSerializerOptions.Web));

    private static void AssertAllApplied(SyncCall call) => Assert.All(call.Response.Results, result => Assert.Equal("applied", result.Status));

    private static long GzipLength(byte[] body)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(body);
        }

        return output.Length;
    }

    private static string DescribeFailure(Exception exception) =>
        exception switch
        {
            HttpRequestException { StatusCode: { } statusCode } => $"HTTP {(int)statusCode}",
            OperationCanceledException => "timeout",
            HttpRequestException requestException => $"connection error ({requestException.HttpRequestError})",
            HttpIOException ioException => $"connection error ({ioException.HttpRequestError})",
            IOException => "connection error",
            _ => exception.GetType().Name,
        };

    private static string Describe(LimitedApiContainer.ResourceUsage usage) =>
        $"memory current {Mebibytes(usage.MemoryCurrentBytes)}, memory peak since container start {Mebibytes(usage.MemoryPeakBytes)}, "
        + $"CPU used {usage.CpuUsageMicroseconds / 1_000_000.0:F1} s, OOM kills {usage.OomKillCount}";

    private static string Describe(TimeSpan? duration) =>
        duration switch
        {
            null => "-",
            { TotalSeconds: < 1 } => $"{duration.Value.TotalMilliseconds:F0} ms",
            _ => $"{duration.Value.TotalSeconds:F2} s",
        };

    private static string Mebibytes(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} MiB";

    private static TimeSpan? Percentile(IEnumerable<TimeSpan> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[Math.Max(0, (int)Math.Ceiling(percentile * sorted.Length) - 1)];
    }

    private static void ReportSummary(
        List<AccountSummary> accounts,
        List<QueueSummary> queues,
        ConcurrentSummary? concurrent,
        string? containerFailure
    )
    {
        Report("Summary of accounts (first = device B's first sync, delete B = returning device B after the deletions, fresh C = first sync after them):");
        ReportRow(
            [
                "account",
                "seed",
                "ms/cmd",
                "req max",
                "first p50",
                "first B",
                "gzip B",
                "B/item",
                "MiB after",
                "noop p50",
                "noop p95",
                "noop B",
                "token ch",
                "delta",
                "delta B",
                "delete B",
                "del B B",
                "deleted",
                "fresh C",
                "fresh C B",
                "C items",
                "C deleted",
                "cmd rows",
                "B/cmd",
                "item rows",
                "markers",
                "B/item row",
            ]
        );
        foreach (var account in accounts)
        {
            ReportRow(
                [
                    account.Size.Name,
                    Cell(account.Seed, seed => Describe(seed.Total)),
                    Cell(account.Seed, seed => $"{seed.Total.TotalMilliseconds / seed.Commands:F2}"),
                    Cell(account.Seed, seed => Describe(seed.Maximum)),
                    Cell(account.FirstSync, first => Describe(first.Median)),
                    Cell(account.FirstSync, first => first.Bytes.ToString()),
                    Cell(account.FirstSync, first => first.GzipBytes.ToString()),
                    Cell(account.FirstSync, first => $"{(double)first.Bytes / Math.Max(1, first.Items):F0}"),
                    Cell(account.FirstSync, first => $"{first.MemoryAfterBytes / 1024.0 / 1024.0:F1}"),
                    Cell(account.NoOp, noOp => Describe(noOp.Median)),
                    Cell(account.NoOp, noOp => Describe(noOp.Percentile95)),
                    Cell(account.NoOp, noOp => noOp.Bytes.ToString()),
                    Cell(account.NoOp, noOp => noOp.TokenLength.ToString()),
                    Cell(account.Delta, delta => Describe(delta.Duration)),
                    Cell(account.Delta, delta => delta.Bytes.ToString()),
                    Cell(account.Deletion, deletion => Describe(deletion.ReturningDuration)),
                    Cell(account.Deletion, deletion => deletion.ReturningBytes.ToString()),
                    Cell(account.Deletion, deletion => deletion.ReturningDeleted.ToString()),
                    Cell(account.Deletion, deletion => Describe(deletion.FreshDuration)),
                    Cell(account.Deletion, deletion => deletion.FreshBytes.ToString()),
                    Cell(account.Deletion, deletion => deletion.FreshItems.ToString()),
                    Cell(account.Deletion, deletion => deletion.FreshDeleted.ToString()),
                    Cell(account.Database, database => database.ProcessedCommands.ToString()),
                    Cell(account.Database, database => $"{(double)database.ProcessedCommandsBytes / database.ProcessedCommands:F0}"),
                    Cell(account.Database, database => database.Items.ToString()),
                    Cell(account.Database, database => database.DeletedItems.ToString()),
                    Cell(account.Database, database => $"{(double)database.ItemsBytes / database.Items:F0}"),
                ]
            );
        }

        Report($"Summary of queue lengths (* = above the {IngressTimeout.TotalSeconds:F0} s ingress timeout):");
        ReportRow(["length", "body B", "status", "duration", "ms/cmd", "applied", "MiB before", "MiB after"]);
        foreach (var queue in queues)
        {
            ReportRow(
                [
                    queue.Length.ToString(),
                    queue.RequestBytes.ToString(),
                    queue.Status,
                    $"{Describe(queue.Duration)}{(queue.Duration > IngressTimeout ? " *" : "")}",
                    $"{queue.Duration.TotalMilliseconds / queue.Length:F2}",
                    queue.Applied?.ToString() ?? "-",
                    $"{queue.MemoryBeforeBytes / 1024.0 / 1024.0:F1}",
                    $"{queue.MemoryAfterBytes / 1024.0 / 1024.0:F1}",
                ]
            );
        }

        Report(
            concurrent is null
                ? "Concurrent first syncs: not run"
                : $"Concurrent first syncs ({ConcurrentFirstSyncCount}x {concurrent.Account}): median {Describe(concurrent.Median)}, "
                    + $"max {Describe(concurrent.Maximum)}, failures: {concurrent.Failures}, "
                    + $"memory peak since container start {Mebibytes(concurrent.MemoryPeakBytes)}"
        );
        Report(containerFailure ?? "The API container survived every step.");
    }

    private static string Cell<T>(T? value, Func<T, string> format)
        where T : class => value is null ? "-" : format(value);

    private static void ReportRow(string[] cells) => Report(string.Join(" | ", cells.Select(cell => cell.PadLeft(10))));

    private static void Report(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    private sealed record AccountSize(int Lists, int ItemsPerList)
    {
        public string Name => $"{Lists}x{ItemsPerList}";
    }

    private sealed record SyncCall(TimeSpan Duration, byte[] ResponseBody, SyncResponse Response);

    private sealed class AccountSummary(AccountSize size, string authUserId)
    {
        public AccountSize Size { get; } = size;

        public string AuthUserId { get; } = authUserId;

        public SeedSummary? Seed { get; set; }

        public FirstSyncSummary? FirstSync { get; set; }

        public NoOpSummary? NoOp { get; set; }

        public DeltaSummary? Delta { get; set; }

        public DeletionSummary? Deletion { get; set; }

        public DatabaseSizes? Database { get; set; }
    }

    private sealed record SeedSummary(TimeSpan Total, TimeSpan Median, TimeSpan Maximum, int Commands);

    private sealed record FirstSyncSummary(TimeSpan Median, long Bytes, long GzipBytes, int Lists, int Items, long MemoryAfterBytes);

    private sealed record NoOpSummary(TimeSpan Median, TimeSpan Percentile95, long Bytes, int TokenLength);

    private sealed record DeltaSummary(TimeSpan Duration, long Bytes, int Items);

    private sealed record DeletionSummary(
        TimeSpan ReturningDuration,
        long ReturningBytes,
        int ReturningDeleted,
        TimeSpan FreshDuration,
        long FreshBytes,
        int FreshItems,
        int FreshDeleted
    );

    private sealed record DatabaseSizes(long ProcessedCommands, long ProcessedCommandsBytes, long Items, long DeletedItems, long ItemsBytes);

    private sealed record QueueSummary(
        int Length,
        long RequestBytes,
        string Status,
        TimeSpan Duration,
        int? Applied,
        long MemoryBeforeBytes,
        long MemoryAfterBytes
    );

    private sealed record ConcurrentSummary(string Account, TimeSpan? Median, TimeSpan? Maximum, string Failures, long MemoryPeakBytes);
}
