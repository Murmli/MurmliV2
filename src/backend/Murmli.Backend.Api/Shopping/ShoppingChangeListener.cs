using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Murmli.Backend.Api.Shopping;

internal sealed partial class ShoppingChangeListener(
    IConfiguration configuration,
    SyncEventHub hub,
    TimeProvider timeProvider,
    ILogger<ShoppingChangeListener> logger
) : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The DbContext's connection string has the password stripped (Persist Security Info=false)
        var connectionString = configuration.GetConnectionString("murmli")
            ?? throw new InvalidOperationException("There is no connection string to listen for shopping changes.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(connectionString, stoppingToken);
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

    private async Task ListenAsync(string connectionString, CancellationToken stoppingToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(stoppingToken);
        connection.Notification += (_, notification) => OnNotification(notification.Payload);
        await using (var command = new NpgsqlCommand($"LISTEN {ShoppingChangeNotification.Channel}", connection))
        {
            await command.ExecuteNonQueryAsync(stoppingToken);
        }

        // Notifications sent while no connection was listening are lost, every open stream has to pull once
        hub.PokeEveryone();
        while (true)
        {
            await connection.WaitAsync(stoppingToken);
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
