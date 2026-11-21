using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Data;
using Npgsql;

namespace Murmli.Backend.Api.Shopping;

internal sealed partial class ShoppingChangeListener(
    IServiceScopeFactory scopeFactory,
    SyncEventHub hub,
    TimeProvider timeProvider,
    ILogger<ShoppingChangeListener> logger
) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogListenerFailed(exception);
                try
                {
                    await Task.Delay(ReconnectDelay, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MurmliDbContext>().Database;
        // The context's connection authenticates like every other database access, in Azure with the Entra token
        await database.OpenConnectionAsync(stoppingToken);
        var connection = (NpgsqlConnection)database.GetDbConnection();
        connection.Notification += (_, notification) => OnNotification(notification.Payload);
        await using (var command = new NpgsqlCommand($"LISTEN {ShoppingChangeNotification.Channel}", connection))
        {
            await command.ExecuteNonQueryAsync(stoppingToken);
        }

        // Notifications sent while no connection was listening are lost, every open stream has to pull once
        hub.PokeEveryone();
        while (true)
        {
            if (!await connection.WaitAsync(ProbeInterval, stoppingToken))
            {
                // A silently dropped connection only fails on use, waiting alone would never notice
                await using var probe = new NpgsqlCommand("SELECT 1", connection);
                await probe.ExecuteNonQueryAsync(stoppingToken);
            }
        }
    }

    private void OnNotification(string payload)
    {
        try
        {
            if (JsonSerializer.Deserialize<ShoppingChangeNotification>(payload, JsonSerializerOptions.Web) is { } notification)
            {
                hub.Poke(notification.UserIds);
            }
        }
        catch (JsonException exception)
        {
            LogInvalidNotification(exception);
        }
    }
}
