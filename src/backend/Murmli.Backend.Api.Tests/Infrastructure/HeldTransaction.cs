using Npgsql;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class HeldTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
{
    public Task CommitAsync() => transaction.CommitAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        // Disposing an uncommitted transaction rolls it back
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
