using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.Configuration;

namespace Murmli.Backend.Api.Sync;

internal sealed class SyncCleanupService(SyncCleanupRunner runner, TimeProvider timeProvider, IOptions<SyncOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cleanupOptions = options.Value.Cleanup;
        if (!cleanupOptions.Enabled)
        {
            return;
        }

        await Task.Delay(cleanupOptions.InitialDelay, timeProvider, stoppingToken);
        while (true)
        {
            await runner.RunAsync(stoppingToken);
            await Task.Delay(cleanupOptions.Interval, timeProvider, stoppingToken);
        }
    }
}
