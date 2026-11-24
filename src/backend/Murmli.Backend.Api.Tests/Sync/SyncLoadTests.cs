using System.Diagnostics;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Sync;

[Collection<SyncLoadCollection>]
public class SyncLoadTests(SyncLoadEnvironment environment)
{
    private const int ConcurrentFirstSyncs = 10;

    private const int GrowthListCount = 5;

    [Fact(Explicit = true)]
    public async Task Sync_FirstSyncOfLargestAccount_ReportsDurationSizesAndGzipCost()
    {
        var account = await environment.GetLargestAccountAsync();
        var settings = environment.Settings;
        var items = settings.Lists * settings.ItemsPerList;

        var (gzipRequests, gzipWindow) = await MeasureFirstSyncsAsync(account, acceptGzip: true);
        SyncLoadReport.Write(
            "firstSync",
            "gzip",
            [.. SyncLoadReport.Of(gzipRequests), .. SyncLoadReport.Of(gzipWindow), ("cpuMillisecondsPerRequest", gzipWindow.CpuMilliseconds / gzipWindow.Units)]
        );
        AssertFullStates(gzipRequests, compressed: true);
        var (identityRequests, identityWindow) = await MeasureFirstSyncsAsync(account, acceptGzip: false);
        SyncLoadReport.Write(
            "firstSync",
            "identity",
            [
                .. SyncLoadReport.Of(identityRequests),
                .. SyncLoadReport.Of(identityWindow),
                ("cpuMillisecondsPerRequest", identityWindow.CpuMilliseconds / identityWindow.Units),
                ("bodyBytesPerItem", (double)identityRequests.First.Body.Length / items),
            ]
        );
        AssertFullStates(identityRequests, compressed: false);
        SyncLoadReport.Write(
            "firstSync",
            "gzipCost",
            ("cpuMillisecondsPerRequest", gzipWindow.CpuMilliseconds / gzipWindow.Units - identityWindow.CpuMilliseconds / identityWindow.Units),
            ("compressionFactor", (double)gzipRequests.First.Body.Length / gzipRequests.First.WireBytes)
        );
    }

    [Fact(Explicit = true)]
    public async Task Sync_NoChangePoll_ReportsDuration()
    {
        var account = await environment.GetLargestAccountAsync();
        var device = account.Writer.OtherDevice();
        var token = (await device.SyncAsync(token: null, [])).ReadResponse().Token;
        var requests = new SyncLoadRequests();

        var window = await environment.MeasureAsync(async () =>
        {
            requests.Add(await device.SyncAsync(token, []));
            return true;
        });

        SyncLoadReport.Write(
            "noChangePoll",
            "poll",
            [.. SyncLoadReport.Of(requests), .. SyncLoadReport.Of(window), ("cpuMillisecondsPerRequest", window.CpuMilliseconds / window.Units)]
        );
        requests.AssertAllOkWithSameBodyLength();
        var response = requests.First.ReadResponse();
        Assert.Empty(response.Scopes);
        Assert.Equal(token, response.Token);
    }

    [Fact(Explicit = true)]
    public async Task Sync_SmallChange_ReportsWriterAndReaderDuration()
    {
        var account = await environment.GetLargestAccountAsync();
        var writer = account.Writer.OtherDevice();
        var reader = account.Writer.OtherDevice();
        var writerToken = (await writer.SyncAsync(token: null, [])).ReadResponse().Token;
        var readerToken = (await reader.SyncAsync(token: null, [])).ReadResponse().Token;
        var listId = account.ListIds[0];
        var itemId = account.ItemIds[0][0];
        var writes = new SyncLoadRequests();
        var reads = new SyncLoadRequests();

        var window = await environment.MeasureAsync(async () =>
        {
            var command = SyncLoadCommands.SetItemName(listId, itemId, TimeProvider.System.GetUtcNow(), writes.Count % 2 == 0 ? "Milch 10" : "Milch 11");
            var write = await writer.SyncAsync(writerToken, [command]);
            writes.Add(write);
            var writeResponse = write.ReadResponse();
            Assert.Equal([new SyncCommandResult(SyncTestCommands.GetId(command), "applied")], writeResponse.Results);
            writerToken = writeResponse.Token;
            var read = await reader.SyncAsync(readerToken, []);
            reads.Add(read);
            var readResponse = read.ReadResponse();
            var block = Assert.Single(readResponse.Scopes);
            Assert.Equal((listId, false, 1, 0), (block.ScopeId, block.FullState, block.Records.Count, block.DeletedRecords.Count));
            readerToken = readResponse.Token;
            return true;
        });

        SyncLoadReport.Write("smallChange", "writer", SyncLoadReport.Of(writes));
        SyncLoadReport.Write(
            "smallChange",
            "reader",
            [.. SyncLoadReport.Of(reads), .. SyncLoadReport.Of(window), ("cpuMillisecondsPerChange", window.CpuMilliseconds / window.Units)]
        );
    }

    [Fact(Explicit = true)]
    public async Task Sync_TenConcurrentFirstSyncsOfLargestAccount_ReportsDurationAndMemory()
    {
        var account = await environment.GetLargestAccountAsync();
        var requests = new SyncLoadRequests();

        var window = await environment.MeasureAsync(async () =>
        {
            foreach (var exchange in await Task.WhenAll(Enumerable.Range(0, ConcurrentFirstSyncs).Select(_ => account.Writer.OtherDevice().SyncAsync(token: null, []))))
            {
                requests.Add(exchange);
            }

            return true;
        });

        SyncLoadReport.Write("concurrentFirstSyncs", "burst", [("bursts", window.Units), .. SyncLoadReport.Of(requests), .. SyncLoadReport.Of(window)]);
        AssertFullStates(requests, compressed: true);
    }

    [Fact(Explicit = true)]
    public async Task Sync_ManyDeletions_ReportsReturningAndFreshDevice()
    {
        await environment.StartAsync();
        var settings = environment.Settings;
        var deletions = settings.Lists * settings.ItemsPerList;
        var content = await SyncLoadContent.CreateAsync(settings.Seed);
        var account = await SyncLoadAccount.CreateAsync(environment.HttpClient, settings.Lists);
        await environment.AssertApiSurvivesAsync(() => account.FillAsync(settings.ItemsPerList, content));
        var returning = account.Writer.OtherDevice();
        var returningToken = (await returning.SyncAsync(token: null, [])).ReadResponse().Token;
        await environment.AssertApiSurvivesAsync(account.DeleteAllItemsAsync);

        var returningRequests = new SyncLoadRequests();
        var returningWindow = await environment.MeasureAsync(async () =>
        {
            returningRequests.Add(await returning.SyncAsync(returningToken, []));
            return true;
        });
        SyncLoadReport.Write(
            "manyDeletions",
            "returningDevice",
            [
                .. SyncLoadReport.Of(returningRequests),
                .. SyncLoadReport.Of(returningWindow),
                ("deletions", deletions),
                ("bodyBytesPerDeletion", (double)returningRequests.First.Body.Length / deletions),
                ("cpuMillisecondsPerRequest", returningWindow.CpuMilliseconds / returningWindow.Units),
            ]
        );
        returningRequests.AssertAllOkWithSameBodyLength();
        Assert.False(returningRequests.First.Compressed);
        var delta = returningRequests.First.ReadResponse();
        Assert.Equal(settings.Lists, delta.Scopes.Count);
        Assert.All(
            delta.Scopes,
            block => Assert.Equal((false, 0, settings.ItemsPerList), (block.FullState, block.Records.Count, block.DeletedRecords.Count))
        );

        await environment.AssertApiSurvivesAsync(() => account.FillAsync(settings.ItemsPerList, content));
        var (freshRequests, freshWindow) = await MeasureFirstSyncsAsync(account, acceptGzip: true);
        SyncLoadReport.Write(
            "manyDeletions",
            "freshDevice",
            [
                .. SyncLoadReport.Of(freshRequests),
                .. SyncLoadReport.Of(freshWindow),
                ("tombstones", deletions),
                ("cpuMillisecondsPerRequest", freshWindow.CpuMilliseconds / freshWindow.Units),
            ]
        );
        AssertFullStates(freshRequests, compressed: true);
    }

    [Fact(Explicit = true)]
    public async Task Sync_CommandsPerRequestAndQueue_ReportsCostPerCommand()
    {
        await environment.StartAsync();
        var settings = environment.Settings;
        var account = await SyncLoadAccount.CreateAsync(environment.HttpClient, SyncLoadSettings.MaxLists);
        foreach (var commandsPerRequest in new[] { 1, 50, account.MaxCommandsPerRequest })
        {
            var content = await SyncLoadContent.CreateAsync(settings.Seed);
            var requests = new SyncLoadRequests();
            var commands = 0;
            var window = await environment.MeasureAsync(async () =>
            {
                var creates = account.TakeItemCreates(commandsPerRequest, content);
                if (creates is null)
                {
                    return false;
                }

                foreach (var exchange in await account.ApplyAsync(creates))
                {
                    requests.Add(exchange);
                }

                commands += creates.Count;
                return true;
            });

            SyncLoadReport.Write(
                "commands",
                $"perRequest{commandsPerRequest}",
                [
                    .. SyncLoadReport.Of(requests),
                    .. SyncLoadReport.Of(window),
                    ("commands", commands),
                    ("millisecondsPerCommand", requests.Durations.Sum(duration => duration.TotalMilliseconds) / commands),
                    ("cpuMillisecondsPerCommand", window.CpuMilliseconds / commands),
                ]
            );
            account = await SyncLoadAccount.CreateAsync(environment.HttpClient, SyncLoadSettings.MaxLists);
        }

        var queueContent = await SyncLoadContent.CreateAsync(settings.Seed);
        var drains = new List<TimeSpan>();
        var queueRequests = new SyncLoadRequests();
        var queueWindow = await environment.MeasureAsync(async () =>
        {
            var creates = account.TakeItemCreates(settings.QueueCommands, queueContent);
            if (creates is null)
            {
                return false;
            }

            var started = Stopwatch.GetTimestamp();
            var exchanges = await account.ApplyAsync(creates);
            drains.Add(Stopwatch.GetElapsedTime(started));
            foreach (var exchange in exchanges)
            {
                queueRequests.Add(exchange);
            }

            return true;
        });

        var queuedCommands = drains.Count * settings.QueueCommands;
        SyncLoadReport.Write(
            "commands",
            "queue",
            [
                .. SyncLoadReport.Of(queueRequests),
                .. SyncLoadReport.Of(queueWindow),
                ("queueCommands", settings.QueueCommands),
                ("drains", drains.Count),
                ("medianDrainMilliseconds", SyncLoadReport.Percentile(drains, 0.5).TotalMilliseconds),
                ("millisecondsPerCommand", drains.Sum(drain => drain.TotalMilliseconds) / queuedCommands),
                ("cpuMillisecondsPerCommand", queueWindow.CpuMilliseconds / queuedCommands),
            ]
        );
    }

    [Fact(Explicit = true)]
    public async Task Sync_CommandsAndDeletions_ReportsGrowthOfProcessedCommandsAndTombstones()
    {
        await environment.StartAsync();
        var settings = environment.Settings;
        var account = await SyncLoadAccount.CreateAsync(environment.HttpClient, Math.Min(settings.Lists, GrowthListCount));
        var commands = (long)account.ListIds.Count * settings.ItemsPerList;
        var content = await SyncLoadContent.CreateAsync(settings.Seed);
        var before = await environment.ReadSyncTablesAsync(account.ListIds);

        await environment.AssertApiSurvivesAsync(() => account.FillAsync(settings.ItemsPerList, content));
        var created = await environment.ReadSyncTablesAsync(account.ListIds);
        await environment.AssertApiSurvivesAsync(account.DeleteAllItemsAsync);
        var deleted = await environment.ReadSyncTablesAsync(account.ListIds);

        SyncLoadReport.Write(
            "growth",
            "processedCommands",
            ("creates", commands),
            ("bytesPerCreate", (double)(created.ProcessedCommandBytes - before.ProcessedCommandBytes) / commands),
            ("deletes", commands),
            ("bytesPerDelete", (double)(deleted.ProcessedCommandBytes - created.ProcessedCommandBytes) / commands)
        );
        SyncLoadReport.Write("growth", "tombstones", ("deletions", commands), ("bytesPerDeletion", (double)(deleted.TombstoneBytes - created.TombstoneBytes) / commands));
        Assert.Equal(
            (commands, commands, commands),
            (created.ProcessedCommands - before.ProcessedCommands, deleted.ProcessedCommands - created.ProcessedCommands, deleted.Tombstones - created.Tombstones)
        );
    }

    private async Task<(SyncLoadRequests Requests, SyncLoadWindow Window)> MeasureFirstSyncsAsync(SyncLoadAccount account, bool acceptGzip)
    {
        var requests = new SyncLoadRequests();
        var window = await environment.MeasureAsync(async () =>
        {
            requests.Add(await account.Writer.OtherDevice().SyncAsync(token: null, [], acceptGzip));
            return true;
        });
        return (requests, window);
    }

    private void AssertFullStates(SyncLoadRequests requests, bool compressed)
    {
        var settings = environment.Settings;
        requests.AssertAllOkWithSameBodyLength();
        Assert.Equal(compressed, requests.First.Compressed);
        var response = requests.First.ReadResponse();
        Assert.True(response.CoversAllScopes);
        Assert.Equal(settings.Lists + 1, response.Scopes.Count);
        Assert.All(response.Scopes, block => Assert.Equal((true, 0), (block.FullState, block.DeletedRecords.Count)));
        Assert.Equal(settings.Lists * (settings.ItemsPerList + 1), response.Scopes.Sum(block => block.Records.Count));
    }
}
