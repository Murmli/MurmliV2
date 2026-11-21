using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Channels;
using Murmli.Backend.Api.Shopping;
using Murmli.Backend.Api.Tests.Infrastructure;

namespace Murmli.Backend.Api.Tests.Shopping;

public class ShoppingSyncConnectionLoadTests
{
    private const double ApiCpuCount = 0.25;

    private const int SetupConcurrency = 16;

    private const int RampConcurrency = 64;

    private const int PokeSampleCount = 20;

    private const int RandomSeed = 20261121;

    private const int CrashLogLineCount = 30;

    private static readonly TimeSpan IdleSettleTime = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan IdleWindow = TimeSpan.FromSeconds(50);

    private static readonly TimeSpan PokeTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan JitterWindow = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(30);

    [Fact(Explicit = true)]
    public async Task StreamEventsAsync_ThousandsOfIdleStreamsAndReconnectWaves_ReportsCapacity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var streamCounts = (Environment.GetEnvironmentVariable("SYNC_LOAD_STREAM_COUNTS") ?? "1000,3000,5000")
            .Split(',')
            .Select(int.Parse)
            .ToArray();
        var postgresMaximumConnections = int.Parse(Environment.GetEnvironmentVariable("SYNC_LOAD_POSTGRES_MAX_CONNECTIONS") ?? "35");
        int? maximumPoolSize = Environment.GetEnvironmentVariable("SYNC_LOAD_MAXIMUM_POOL_SIZE") is { } poolSize ? int.Parse(poolSize) : null;
        Report(
            $"Knobs: stream counts {string.Join(", ", streamCounts)}, Postgres max_connections {postgresMaximumConnections}, "
                + $"Npgsql Maximum Pool Size {maximumPoolSize?.ToString() ?? "default (100)"}"
        );

        await using var container = await LimitedApiContainer.StartAsync(
            ApiCpuCount,
            postgresMaximumConnections: postgresMaximumConnections,
            maximumPoolSize: maximumPoolSize,
            cancellationToken: cancellationToken
        );
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = container.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var users = await SetUpUsersAsync(client, streamCounts.Max(), cancellationToken);
        var baseline = await container.GetResourceUsageAsync(cancellationToken);
        Report($"Baseline after setup: {Describe(baseline)}");

        var random = new Random(RandomSeed);
        var summaries = new List<StepSummary>();
        string? containerFailure = null;
        try
        {
            foreach (var streamCount in streamCounts)
            {
                Report($"--- {streamCount} streams ---");
                var stepUsers = users[..streamCount];
                var summary = new StepSummary(streamCount);
                summaries.Add(summary);
                Func<Task>[] phases =
                [
                    async () => summary.Ramp = await RampAsync(client, stepUsers, cancellationToken),
                    async () => summary.Idle = await MeasureIdleAsync(container, stepUsers, baseline, cancellationToken),
                    async () => summary.Pokes = await MeasurePokesAsync(client, stepUsers, random, cancellationToken),
                    async () =>
                        summary.WaveWithoutJitter = await RunWaveAsync(container, client, stepUsers, TimeSpan.Zero, random, cancellationToken),
                    async () =>
                        summary.WaveWithJitter = await RunWaveAsync(container, client, stepUsers, JitterWindow, random, cancellationToken),
                ];
                foreach (var phase in phases)
                {
                    if (await RunPhaseAsync(phase, container, client, cancellationToken) is { } failure)
                    {
                        containerFailure = $"The API container did not survive the step with {streamCount} streams: {failure}";
                        break;
                    }
                }

                if (containerFailure is not null)
                {
                    break;
                }
            }
        }
        finally
        {
            await Task.WhenAll(users.Select(user => user.CloseStreamAsync()));
            ReportSummary(summaries, baseline);
        }

        Assert.True(containerFailure is null, containerFailure);
    }

    private static async Task<LoadUser[]> SetUpUsersAsync(HttpClient client, int userCount, CancellationToken cancellationToken)
    {
        var users = Enumerable.Range(0, userCount).Select(index => new LoadUser($"LoadUser{index:D6}", Guid.NewGuid())).ToArray();
        var start = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(
            users,
            new ParallelOptions { MaxDegreeOfParallelism = SetupConcurrency, CancellationToken = cancellationToken },
            async (user, token) =>
            {
                using (var request = CreateRequest(HttpMethod.Get, "/v1/users/me", user.AuthUserId))
                using (var response = await client.SendAsync(request, token))
                {
                    response.EnsureSuccessStatusCode();
                }

                var setup = await SyncAsync(client, user, [Command("createList", new { listId = user.ListId, name = "Groceries" })], token);
                Assert.Equal("applied", Assert.Single(setup.Results).Status);
            }
        );
        var duration = Stopwatch.GetElapsedTime(start);
        Report(
            $"Setup: {userCount} users with {userCount * 2} requests in {duration.TotalSeconds:F1} s, "
                + $"{userCount * 2 / duration.TotalSeconds:F0} requests/s"
        );
        return users;
    }

    private static async Task<RampSummary> RampAsync(HttpClient client, LoadUser[] stepUsers, CancellationToken cancellationToken)
    {
        var pendingUsers = stepUsers.Where(user => !user.HasOpenStream).ToArray();
        var failures = new ConcurrentDictionary<string, int>();
        var start = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(
            pendingUsers,
            new ParallelOptions { MaxDegreeOfParallelism = RampConcurrency, CancellationToken = cancellationToken },
            async (user, token) =>
            {
                await user.CloseStreamAsync();
                try
                {
                    user.Stream = await EventStream.OpenAsync(client, user.AuthUserId, token);
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    CountFailure(failures, $"stream {DescribeFailure(exception)}");
                }
            }
        );
        var summary = new RampSummary(Stopwatch.GetElapsedTime(start), failures.Values.Sum(), stepUsers.Count(user => user.HasOpenStream));
        Report(
            $"Ramp: tried {pendingUsers.Length} streams in {summary.Duration.TotalSeconds:F1} s, "
                + $"{summary.OpenStreams} of {stepUsers.Length} open, failures: {DescribeFailures(failures)}"
        );
        return summary;
    }

    private static async Task<IdleSummary> MeasureIdleAsync(
        LimitedApiContainer container,
        LoadUser[] stepUsers,
        LimitedApiContainer.ResourceUsage baseline,
        CancellationToken cancellationToken
    )
    {
        await Task.Delay(IdleSettleTime, cancellationToken);
        var before = await container.GetResourceUsageAsync(cancellationToken);
        var start = Stopwatch.GetTimestamp();
        await Task.Delay(IdleWindow, cancellationToken);
        var after = await container.GetResourceUsageAsync(cancellationToken);
        var window = Stopwatch.GetElapsedTime(start);
        var openStreams = stepUsers.Count(user => user.HasOpenStream);
        var summary = new IdleSummary(
            after.MemoryCurrentBytes,
            (after.CpuUsageMicroseconds - before.CpuUsageMicroseconds) / window.TotalMicroseconds * 100,
            openStreams
        );
        Report(
            $"Idle: {openStreams} of {stepUsers.Length} streams still open after the {window.TotalSeconds:F1} s window, "
                + $"memory current {Mebibytes(summary.MemoryBytes)}, "
                + $"{(summary.MemoryBytes - baseline.MemoryCurrentBytes) / 1024.0 / openStreams:F1} KiB per open stream above the baseline, "
                + $"CPU {summary.CpuPercentOfCore:F2} % of one core = {summary.CpuPercentOfCore / ApiCpuCount:F1} % of the {ApiCpuCount} CPU quota, "
                + $"throttled {(after.ThrottledMicroseconds - before.ThrottledMicroseconds) / 1000.0:F0} ms (summed over the host's CPUs)"
        );
        return summary;
    }

    private static async Task<PokeSummary> MeasurePokesAsync(
        HttpClient client,
        LoadUser[] stepUsers,
        Random random,
        CancellationToken cancellationToken
    )
    {
        var candidates = stepUsers.Where(user => user.HasOpenStream).ToArray();
        random.Shuffle(candidates);
        var requestDurations = new List<TimeSpan>();
        var pokeLatencies = new List<TimeSpan>();
        var failures = new ConcurrentDictionary<string, int>();
        var missingPokes = 0;
        foreach (var user in candidates.Take(PokeSampleCount))
        {
            var pokes = user.Stream!.Pokes.Reader;
            while (pokes.TryRead(out _)) { }

            using var pokeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            pokeTimeout.CancelAfter(PokeTimeout);
            var start = Stopwatch.GetTimestamp();
            var poke = pokes.ReadAsync(pokeTimeout.Token).AsTask();
            try
            {
                await SyncAsync(
                    client,
                    user,
                    [Command("addItem", new { itemId = Guid.NewGuid(), listId = user.ListId, name = "Milk" })],
                    cancellationToken
                );
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                await pokeTimeout.CancelAsync();
                CountFailure(failures, $"sync {DescribeFailure(exception)}");
                continue;
            }

            requestDurations.Add(Stopwatch.GetElapsedTime(start));
            try
            {
                pokeLatencies.Add(Stopwatch.GetElapsedTime(start, await poke));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                missingPokes++;
            }
        }

        var summary = new PokeSummary(Percentile(pokeLatencies, 0.5), Percentile(pokeLatencies, 1), missingPokes);
        Report(
            $"Pokes for {requestDurations.Count + failures.Values.Sum()} users: request median {Describe(Percentile(requestDurations, 0.5))}, "
                + $"max {Describe(Percentile(requestDurations, 1))}; poke median {Describe(summary.Median)}, max {Describe(summary.Maximum)}, "
                + $"missing after {PokeTimeout.TotalSeconds:F0} s {missingPokes}; request failures: {DescribeFailures(failures)}"
        );
        return summary;
    }

    private static async Task<WaveSummary> RunWaveAsync(
        LimitedApiContainer container,
        HttpClient client,
        LoadUser[] stepUsers,
        TimeSpan jitterWindow,
        Random random,
        CancellationToken cancellationToken
    )
    {
        await Task.WhenAll(stepUsers.Select(user => user.CloseStreamAsync()));
        var delays = stepUsers.Select(_ => jitterWindow * random.NextDouble()).ToArray();
        var before = await container.GetResourceUsageAsync(cancellationToken);
        var connectTimes = new ConcurrentBag<TimeSpan>();
        var syncDurations = new ConcurrentBag<TimeSpan>();
        var failures = new ConcurrentDictionary<string, int>();
        var startedAt = DateTime.UtcNow;
        var start = Stopwatch.GetTimestamp();
        await Task.WhenAll(
            stepUsers.Select(
                (user, index) =>
                    Task.Run(
                        async () =>
                        {
                            await Task.Delay(delays[index], cancellationToken);
                            var userStart = Stopwatch.GetTimestamp();
                            try
                            {
                                user.Stream = await EventStream.OpenAsync(client, user.AuthUserId, cancellationToken);
                            }
                            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                            {
                                CountFailure(failures, $"stream {DescribeFailure(exception)}");
                                return;
                            }

                            connectTimes.Add(Stopwatch.GetElapsedTime(userStart));
                            var syncStart = Stopwatch.GetTimestamp();
                            try
                            {
                                await SyncAsync(client, user, [], cancellationToken);
                            }
                            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                            {
                                CountFailure(failures, $"sync {DescribeFailure(exception)}");
                                await user.CloseStreamAsync();
                                return;
                            }

                            syncDurations.Add(Stopwatch.GetElapsedTime(syncStart));
                        },
                        cancellationToken
                    )
            )
        );
        var wallTime = Stopwatch.GetElapsedTime(start);
        var after = await container.GetResourceUsageAsync(cancellationToken);
        var (apiLog, postgresLog) = await container.GetLogsAsync(startedAt, cancellationToken);

        var summary = new WaveSummary(wallTime, failures.Values.Sum(), Percentile(syncDurations, 0.95), after.MemoryPeakBytes);
        Report(
            $"Wave {(jitterWindow == TimeSpan.Zero ? "without jitter" : $"with jitter over {jitterWindow.TotalSeconds:F0} s")}: "
                + $"all {stepUsers.Length} users finished after {wallTime.TotalSeconds:F1} s"
        );
        Report(
            $"  time to connected ({connectTimes.Count} users): median {Describe(Percentile(connectTimes, 0.5))}, "
                + $"95th percentile {Describe(Percentile(connectTimes, 0.95))}, max {Describe(Percentile(connectTimes, 1))}"
        );
        Report(
            $"  sync duration ({syncDurations.Count} users): median {Describe(Percentile(syncDurations, 0.5))}, "
                + $"95th percentile {Describe(summary.SyncDuration95thPercentile)}, max {Describe(Percentile(syncDurations, 1))}"
        );
        Report($"  failures: {DescribeFailures(failures)}");
        Report(
            $"  memory current after the wave {Mebibytes(after.MemoryCurrentBytes)}, "
                + $"memory peak since container start {Mebibytes(after.MemoryPeakBytes)}, "
                + $"CPU used {(after.CpuUsageMicroseconds - before.CpuUsageMicroseconds) / 1_000_000.0:F1} s, "
                + $"throttled {(after.ThrottledMicroseconds - before.ThrottledMicroseconds) / 1_000_000.0:F1} s (summed over the host's CPUs), "
                + $"OOM kills {after.OomKillCount}"
        );
        Report(
            $"  log lines since the wave started: Postgres 'too many clients' {CountLines(postgresLog, "too many clients")}, "
                + $"API 'Connection[20004]' {CountLines(apiLog, "Connection[20004]")}"
        );
        return summary;
    }

    private static async Task<string?> RunPhaseAsync(
        Func<Task> phase,
        LimitedApiContainer container,
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        ExceptionDispatchInfo? phaseFailure = null;
        try
        {
            await phase();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            phaseFailure = ExceptionDispatchInfo.Capture(exception);
        }

        var containerFailure = await FindContainerFailureAsync(container, client, cancellationToken);
        if (containerFailure is null)
        {
            phaseFailure?.Throw();
        }

        return containerFailure;
    }

    private static async Task<string?> FindContainerFailureAsync(
        LimitedApiContainer container,
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        var start = Stopwatch.GetTimestamp();
        while (true)
        {
            var remaining = HealthTimeout - Stopwatch.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero)
            {
                return await DescribeUnresponsiveContainerAsync(container, cancellationToken);
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(remaining);
                using var response = await client.GetAsync("/health", timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    break;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        var usage = await container.GetResourceUsageAsync(cancellationToken);
        return usage.OomKillCount > 0 ? $"the cgroup reports {usage.OomKillCount} OOM kills" : null;
    }

    private static async Task<string> DescribeUnresponsiveContainerAsync(LimitedApiContainer container, CancellationToken cancellationToken)
    {
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

    private static async Task<SyncResponse> SyncAsync(
        HttpClient client,
        LoadUser user,
        SyncCommand[] commands,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = CreateRequest(HttpMethod.Post, "/v1/sync", user.AuthUserId);
        request.Content = JsonContent.Create(new SyncRequest(user.SyncToken, commands), options: JsonSerializerOptions.Web);
        using var response = await client.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var syncResponse = await response.Content.ReadFromJsonAsync<SyncResponse>(JsonSerializerOptions.Web, timeout.Token);
        user.SyncToken = syncResponse!.Token;
        return syncResponse;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string authUserId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(authUserId));
        return request;
    }

    private static SyncCommand Command(string type, object arguments) =>
        new(Guid.NewGuid(), type, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(arguments, JsonSerializerOptions.Web));

    private static void CountFailure(ConcurrentDictionary<string, int> failures, string kind) =>
        failures.AddOrUpdate(kind, 1, (_, count) => count + 1);

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

    private static string DescribeFailures(ConcurrentDictionary<string, int> failures) =>
        failures.IsEmpty ? "none" : string.Join(", ", failures.OrderByDescending(failure => failure.Value).Select(failure => $"{failure.Key} {failure.Value}"));

    private static string Describe(LimitedApiContainer.ResourceUsage usage) =>
        $"memory current {Mebibytes(usage.MemoryCurrentBytes)}, memory peak {Mebibytes(usage.MemoryPeakBytes)}, "
        + $"CPU used {usage.CpuUsageMicroseconds / 1_000_000.0:F1} s, "
        + $"throttled {usage.ThrottledMicroseconds / 1_000_000.0:F1} s (summed over the host's CPUs), "
        + $"OOM kills {usage.OomKillCount}";

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

    private static int CountLines(string log, string text) => log.Split('\n').Count(line => line.Contains(text, StringComparison.Ordinal));

    private static void ReportSummary(List<StepSummary> summaries, LimitedApiContainer.ResourceUsage baseline)
    {
        Report("Summary (0 s = wave without jitter, 30 s = wave with jitter, MiB of a wave = memory peak since container start, read after the wave):");
        ReportRow(
            [
                "N",
                "open",
                "ramp",
                "ramp fail",
                "idle MiB",
                "KiB/stream",
                "CPU %core",
                "%quota",
                "poke p50",
                "poke max",
                "missing",
                "0 s wall",
                "0 s fail",
                "0 s p95",
                "0 s MiB",
                "30 s wall",
                "30 s fail",
                "30 s p95",
                "30 s MiB",
            ]
        );
        foreach (var summary in summaries)
        {
            ReportRow(
                [
                    summary.StreamCount.ToString(),
                    Cell(summary.Ramp, ramp => ramp.OpenStreams.ToString()),
                    Cell(summary.Ramp, ramp => Describe(ramp.Duration)),
                    Cell(summary.Ramp, ramp => ramp.Failures.ToString()),
                    Cell(summary.Idle, idle => $"{idle.MemoryBytes / 1024.0 / 1024.0:F1}"),
                    Cell(summary.Idle, idle => $"{(idle.MemoryBytes - baseline.MemoryCurrentBytes) / 1024.0 / idle.OpenStreams:F1}"),
                    Cell(summary.Idle, idle => $"{idle.CpuPercentOfCore:F2}"),
                    Cell(summary.Idle, idle => $"{idle.CpuPercentOfCore / ApiCpuCount:F1}"),
                    Cell(summary.Pokes, pokes => Describe(pokes.Median)),
                    Cell(summary.Pokes, pokes => Describe(pokes.Maximum)),
                    Cell(summary.Pokes, pokes => pokes.Missing.ToString()),
                    .. WaveCells(summary.WaveWithoutJitter),
                    .. WaveCells(summary.WaveWithJitter),
                ]
            );
        }
    }

    private static string[] WaveCells(WaveSummary? wave) =>
        [
            Cell(wave, value => Describe(value.WallTime)),
            Cell(wave, value => value.Failures.ToString()),
            Cell(wave, value => Describe(value.SyncDuration95thPercentile)),
            Cell(wave, value => $"{value.MemoryPeakBytes / 1024.0 / 1024.0:F1}"),
        ];

    private static string Cell<T>(T? value, Func<T, string> format)
        where T : class => value is null ? "-" : format(value);

    private static void ReportRow(string[] cells) => Report(string.Join(" | ", cells.Select(cell => cell.PadLeft(10))));

    private static void Report(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    private sealed class LoadUser(string authUserId, Guid listId)
    {
        public string AuthUserId { get; } = authUserId;

        public Guid ListId { get; } = listId;

        public string? SyncToken { get; set; }

        public EventStream? Stream { get; set; }

        public bool HasOpenStream => Stream is { IsOpen: true };

        public async Task CloseStreamAsync()
        {
            if (Stream is { } stream)
            {
                Stream = null;
                await stream.CloseAsync();
            }
        }
    }

    private sealed class EventStream
    {
        private readonly HttpResponseMessage response;

        private readonly CancellationTokenSource cancellation;

        private readonly Task reading;

        private EventStream(HttpResponseMessage response, StreamReader reader, CancellationTokenSource cancellation)
        {
            this.response = response;
            this.cancellation = cancellation;
            reading = ReadAsync(reader, cancellation.Token);
        }

        public Channel<long> Pokes { get; } = Channel.CreateUnbounded<long>();

        public bool IsOpen => !reading.IsCompleted;

        public static async Task<EventStream> OpenAsync(HttpClient client, string authUserId, CancellationToken cancellationToken)
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            HttpResponseMessage? response = null;
            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                connectTimeout.CancelAfter(RequestTimeout);
                using var request = CreateRequest(HttpMethod.Get, "/v1/sync/events", authUserId);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectTimeout.Token);
                response.EnsureSuccessStatusCode();
                var reader = new StreamReader(await response.Content.ReadAsStreamAsync(connectTimeout.Token));
                var firstLine = await reader.ReadLineAsync(connectTimeout.Token);
                if (firstLine != ": connected")
                {
                    throw new InvalidDataException($"The event stream started with '{firstLine}' instead of ': connected'.");
                }

                return new EventStream(response, reader, cancellation);
            }
            catch
            {
                response?.Dispose();
                cancellation.Dispose();
                throw;
            }
        }

        public async Task CloseAsync()
        {
            await cancellation.CancelAsync();
            await reading;
            response.Dispose();
            cancellation.Dispose();
        }

        private async Task ReadAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            using (reader)
            {
                try
                {
                    while (await reader.ReadLineAsync(cancellationToken) is { } line)
                    {
                        if (line == "event: poke")
                        {
                            Pokes.Writer.TryWrite(Stopwatch.GetTimestamp());
                        }
                    }
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or HttpRequestException) { }
            }
        }
    }

    private sealed class StepSummary(int streamCount)
    {
        public int StreamCount { get; } = streamCount;

        public RampSummary? Ramp { get; set; }

        public IdleSummary? Idle { get; set; }

        public PokeSummary? Pokes { get; set; }

        public WaveSummary? WaveWithoutJitter { get; set; }

        public WaveSummary? WaveWithJitter { get; set; }
    }

    private sealed record RampSummary(TimeSpan Duration, int Failures, int OpenStreams);

    private sealed record IdleSummary(long MemoryBytes, double CpuPercentOfCore, int OpenStreams);

    private sealed record PokeSummary(TimeSpan? Median, TimeSpan? Maximum, int Missing);

    private sealed record WaveSummary(TimeSpan WallTime, int Failures, TimeSpan? SyncDuration95thPercentile, long MemoryPeakBytes);
}
