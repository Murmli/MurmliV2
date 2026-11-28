namespace Murmli.Backend.Api.Tests.Infrastructure;

public sealed class SyncApiFixture : IAsyncLifetime
{
    private readonly PostgresFixture postgres = new();

    internal ApiHost Api { get; private set; } = null!;

    internal string ConnectionString { get; private set; } = "";

    public string CreateDatabaseConnectionString() => postgres.CreateDatabaseConnectionString();

    public async ValueTask InitializeAsync()
    {
        await postgres.InitializeAsync();
        ConnectionString = postgres.CreateDatabaseConnectionString();
        Api = await ApiHost.StartAsync(ConnectionString, testDataTypes: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (Api is not null)
        {
            await Api.DisposeAsync();
        }

        await postgres.DisposeAsync();
    }
}
