using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;

internal sealed class SyncSimulationEnvironment : IAsyncDisposable
{
    private SyncSimulationEnvironment(IReadOnlyList<ApiHost> apis, IReadOnlyList<string> apiUrls, SyncSimulationControlApp control)
    {
        Apis = apis;
        ApiUrls = apiUrls;
        Control = control;
    }

    public IReadOnlyList<ApiHost> Apis { get; }

    public IReadOnlyList<string> ApiUrls { get; }

    public SyncSimulationControlApp Control { get; }

    public static async Task<SyncSimulationEnvironment> StartAsync(string connectionString)
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds()));
        var configuration = new Dictionary<string, string?>
        {
            ["Sync:Retention"] = Environment.GetEnvironmentVariable("SYNC_SIMULATION_RETENTION") ?? "01:00:00",
            ["Sync:Cleanup:SafetyMargin"] = "00:10:00",
            ["Database:MaxRetryCount"] = "10",
            ["Database:MaxRetryDelay"] = "00:00:00.200",
            ["Logging:Console:LogLevel:Default"] = "Warning",
        };
        var apis = new List<ApiHost>();
        try
        {
            // One after the other: two hosts running the test DDL at the same time fail with 23505
            apis.Add(await ApiHost.StartAsync(connectionString, configuration, testDataTypes: true, timeProvider: timeProvider));
            apis.Add(await ApiHost.StartAsync(connectionString, configuration, testDataTypes: true, timeProvider: timeProvider));
            var apiUrls = apis.Select(api => api.HttpClient.BaseAddress!.GetLeftPart(UriPartial.Authority)).ToList();
            var control = await SyncSimulationControlApp.StartAsync(
                apiUrls,
                timeProvider,
                new DatabaseAdministration(connectionString),
                apis[0].Services.GetRequiredService<SyncCleanupRunner>()
            );
            return new SyncSimulationEnvironment(apis, apiUrls, control);
        }
        catch
        {
            foreach (var api in apis)
            {
                await api.DisposeAsync();
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Control.DisposeAsync();
        foreach (var api in Apis.Reverse())
        {
            await api.DisposeAsync();
        }
    }
}
