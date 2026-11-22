using System.Diagnostics;
using Npgsql;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class DatabaseAdministration(string connectionString)
{
    private static readonly TimeSpan LockWaiterTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan LockWaiterPollInterval = TimeSpan.FromMilliseconds(20);

    private readonly NpgsqlConnectionStringBuilder connectionStringBuilder = new(connectionString) { Pooling = false };

    public async Task<Guid> ReadDatabaseIdentityAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT value FROM sync.database_identity", connection);
        return Assert.IsType<Guid>(await command.ExecuteScalarAsync(cancellationToken));
    }

    public Task RotateDatabaseIdentityAsync() => UpdateSingleRowAsync("UPDATE sync.database_identity SET value = gen_random_uuid()");

    public Task RaiseTombstoneFloorAsync(Guid scopeId, long floor) =>
        UpdateSingleRowAsync("UPDATE sync.scopes SET tombstone_floor = @floor WHERE id = @target", ("floor", floor), ("target", scopeId));

    public async Task<Guid> InsertMemberEntryAsync(
        Guid scopeId,
        Guid accountId,
        DateTimeOffset createdAt,
        string role = "member",
        string state = "active"
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteSingleRowAsync(
            connection,
            transaction,
            "UPDATE sync.scopes SET version = version + 1 WHERE id = @scope",
            ("scope", scopeId)
        );
        await ExecuteSingleRowAsync(
            connection,
            transaction,
            """
            INSERT INTO sync.member_entries (id, scope_id, account_id, name, role, state, created_at)
            VALUES (@id, @scope, @account, '', @role, @state, @createdAt)
            """,
            ("id", memberId),
            ("scope", scopeId),
            ("account", accountId),
            ("role", role),
            ("state", state),
            ("createdAt", createdAt)
        );
        await transaction.CommitAsync(cancellationToken);
        return memberId;
    }

    public Task<HeldTransaction> LockScopeRowAsync(Guid scopeId) =>
        HoldAsync("SELECT id FROM sync.scopes WHERE id = @target FOR UPDATE", scopeId);

    public Task<HeldTransaction> LockScopesTableAsync() => HoldAsync("LOCK TABLE sync.scopes IN SHARE MODE", target: null);

    public Task<HeldTransaction> LockProcessedCommandsTableAsync() =>
        HoldAsync("LOCK TABLE sync.processed_commands IN SHARE MODE", target: null);

    public Task<HeldTransaction> LockNotesTableAsync() => HoldAsync("LOCK TABLE synctest.notes IN SHARE MODE", target: null);

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

    private static async Task ExecuteSingleRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        Assert.Equal(1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    private async Task UpdateSingleRowAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteSingleRowAsync(connection, transaction: null, sql, parameters);
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
