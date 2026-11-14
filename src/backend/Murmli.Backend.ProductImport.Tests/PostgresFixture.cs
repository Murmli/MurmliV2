using Npgsql;
using Testcontainers.PostgreSql;

namespace Murmli.Backend.ProductImport.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18").Build();

    public string CreateDatabaseConnectionString() =>
        new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = $"test_{Guid.NewGuid():N}",
        }.ConnectionString;

    public ValueTask InitializeAsync() => new(container.StartAsync(TestContext.Current.CancellationToken));

    public ValueTask DisposeAsync() => container.DisposeAsync();
}
