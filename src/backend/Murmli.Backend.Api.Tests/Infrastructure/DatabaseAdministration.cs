using System.Diagnostics;
using Npgsql;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class DatabaseAdministration(string connectionString)
{
    private static readonly TimeSpan LockWaiterTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan LockWaiterPollInterval = TimeSpan.FromMilliseconds(20);

    private readonly NpgsqlConnectionStringBuilder connectionStringBuilder = new(connectionString) { Pooling = false };

    public Task<HeldTransaction> LockScopeRowAsync(Guid scopeId) =>
        HoldAsync("SELECT id FROM sync.scopes WHERE id = @target FOR UPDATE", scopeId);

    public Task<HeldTransaction> LockScopesTableAsync() => HoldAsync("LOCK TABLE sync.scopes IN SHARE MODE", target: null);

    public Task<HeldTransaction> BeginUserDeletionAsync(Guid userId) =>
        HoldAsync("DELETE FROM identity.users WHERE id = @target RETURNING id", userId);

    public async Task WaitForLockWaitersAsync(int count)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = @database AND wait_event_type = 'Lock'",
            connection
        );
        command.Parameters.AddWithValue("database", connectionStringBuilder.Database!);
        var start = Stopwatch.GetTimestamp();
        while ((long)(await command.ExecuteScalarAsync(cancellationToken))! < count)
        {
            Assert.True(
                Stopwatch.GetElapsedTime(start) < LockWaiterTimeout,
                $"Fewer than {count} sessions waited for a lock within {LockWaiterTimeout.TotalSeconds} seconds."
            );
            await Task.Delay(LockWaiterPollInterval, cancellationToken);
        }
    }

    private async Task<HeldTransaction> HoldAsync(string sql, Guid? target)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            if (target is { } targetId)
            {
                command.Parameters.AddWithValue("target", targetId);
                Assert.NotNull(await command.ExecuteScalarAsync(cancellationToken));
            }
            else
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return new HeldTransaction(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
