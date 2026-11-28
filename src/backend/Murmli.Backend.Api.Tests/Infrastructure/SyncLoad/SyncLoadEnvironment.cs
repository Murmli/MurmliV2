using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

public sealed class SyncLoadEnvironment : IAsyncLifetime
{
    private const string ApiImage = "mcr.microsoft.com/dotnet/aspnet:11.0";

    private const string ApiDirectory = "/app";

    private const ushort ApiPort = 8080;

    private const long ApiNanoCpus = 250_000_000;

    private const long ApiMemoryBytes = 512L * 1024 * 1024;

    private const long DatabaseNanoCpus = 1_000_000_000;

    private const long DatabaseMemoryBytes = 2L * 1024 * 1024 * 1024;

    private const string DatabaseAlias = "postgres";

    private const string DatabaseName = "murmli";

    private const string NotStartedMessage = "The load environment has not started.";

    private const string RuntimeVersionVariable = "DOTNET_VERSION=";

    private static readonly string[] DurableSettings = ["fsync", "full_page_writes", "synchronous_commit"];

    private static readonly string[] ReportedSettings = [.. DurableSettings, "max_connections", "superuser_reserved_connections", "server_version"];

    private Task? startup;

    private Task<SyncLoadAccount>? largestAccount;

    private SyncLoadSettings? settings;

    private string? publishDirectory;

    private INetwork? network;

    private PostgreSqlContainer? database;

    private IContainer? api;

    private DockerClient? docker;

    private HttpClient? httpClient;

    internal SyncLoadSettings Settings => settings ?? throw new InvalidOperationException(NotStartedMessage);

    internal HttpClient HttpClient => httpClient ?? throw new InvalidOperationException(NotStartedMessage);

    private PostgreSqlContainer DatabaseContainer => database ?? throw new InvalidOperationException(NotStartedMessage);

    private IContainer ApiContainer => api ?? throw new InvalidOperationException(NotStartedMessage);

    private DockerClient DockerEngine => docker ?? throw new InvalidOperationException(NotStartedMessage);

    // xUnit creates collection fixtures also when every test of the collection is filtered out as explicit, so nothing starts here
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await StopAsync();

    internal Task StartAsync() => startup is { IsFaulted: false, IsCanceled: false } ? startup : startup = StartCoreAsync();

    internal Task<SyncLoadAccount> GetLargestAccountAsync() =>
        largestAccount is { IsFaulted: false, IsCanceled: false } ? largestAccount : largestAccount = CreateLargestAccountAsync();

    internal async Task<SyncLoadWindow> MeasureAsync(Func<Task<bool>> runUnit)
    {
        await Task.Delay(Settings.Settle, TestContext.Current.CancellationToken);
        var start = await ReadApiContainerAsync();
        var stopwatch = Stopwatch.StartNew();
        var units = 0;
        try
        {
            while (stopwatch.Elapsed < Settings.Window && await runUnit())
            {
                units++;
            }
        }
        catch
        {
            await AssertApiRunningAsync();
            throw;
        }

        var end = await ReadApiContainerAsync();
        await AssertApiRunningAsync();
        return new SyncLoadWindow(units, start, end, ApiNanoCpus / 1_000_000_000d);
    }

    internal async Task<(long ProcessedCommandBytes, long TombstoneBytes, long ProcessedCommands, long Tombstones)> ReadSyncTablesAsync(IReadOnlyList<Guid> scopeIds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenDatabaseConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_total_relation_size('sync.processed_commands'), pg_total_relation_size('sync.tombstones'), (SELECT count(*) FROM sync.processed_commands WHERE scope_id = ANY(@scopeIds)), (SELECT count(*) FROM sync.tombstones WHERE scope_id = ANY(@scopeIds))",
            connection
        );
        command.Parameters.AddWithValue("scopeIds", scopeIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    internal async Task AssertApiSurvivesAsync(Func<Task> run)
    {
        try
        {
            await run();
        }
        catch
        {
            await AssertApiRunningAsync();
            throw;
        }

        await AssertApiRunningAsync();
    }

    private static async Task PublishApiAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var project =
            typeof(SyncLoadEnvironment).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "ApiHostProject").Value
            ?? throw new InvalidOperationException("The test project names no API host project.");
        var startInformation = new ProcessStartInfo("dotnet", ["publish", project, "-c", "Release", "-p:UseAppHost=false", "-o", outputDirectory])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInformation) ?? throw new InvalidOperationException("dotnet publish did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        Assert.True(process.ExitCode == 0, $"dotnet publish failed: {await output}{await error}");
    }

    private static void LimitResources(CreateContainerParameters parameters, long nanoCpus, long memoryBytes)
    {
        var hostConfiguration = parameters.HostConfig ?? throw new InvalidOperationException("Testcontainers passed no host config.");
        hostConfiguration.NanoCPUs = nanoCpus;
        hostConfiguration.Memory = memoryBytes;
        hostConfiguration.MemorySwap = memoryBytes;
    }

    private async Task StopAsync()
    {
        httpClient?.Dispose();
        httpClient = null;
        docker?.Dispose();
        docker = null;
        if (api is not null)
        {
            await api.DisposeAsync();
            api = null;
        }

        if (database is not null)
        {
            await database.DisposeAsync();
            database = null;
        }

        if (network is not null)
        {
            await network.DisposeAsync();
            network = null;
        }

        if (publishDirectory is not null)
        {
            Directory.Delete(publishDirectory, recursive: true);
            publishDirectory = null;
        }

        settings = null;
    }

    private async Task StartCoreAsync()
    {
        try
        {
            await CreateEnvironmentAsync();
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    private async Task CreateEnvironmentAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        settings = SyncLoadSettings.FromEnvironment();
        publishDirectory = Directory.CreateTempSubdirectory("murmli-sync-load-").FullName;
        await PublishApiAsync(publishDirectory, cancellationToken);
        network = new NetworkBuilder().Build();
        await network.CreateAsync(cancellationToken);
        docker = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(ResourceReaper.DefaultSessionId).Build();
        database = new PostgreSqlBuilder("postgres:18")
            .WithDatabase(DatabaseName)
            // Appended after the builder's own fsync, full_page_writes and synchronous_commit off, the last value wins
            .WithCommand("-c", "fsync=on", "-c", "full_page_writes=on", "-c", "synchronous_commit=on", "-c", "max_connections=35", "-c", "superuser_reserved_connections=0")
            .WithNetwork(network)
            .WithNetworkAliases(DatabaseAlias)
            .WithCreateParameterModifier(parameters => LimitResources(parameters, DatabaseNanoCpus, DatabaseMemoryBytes))
            .Build();
        await database.StartAsync(cancellationToken);
        var databaseHostConfiguration =
            (await docker.Containers.InspectContainerAsync(database.Id, cancellationToken)).HostConfig
            ?? throw new InvalidOperationException("Docker returned no host config.");
        var databaseSettings = await ReadDatabaseSettingsAsync();
        SyncLoadReport.WriteText(
            "environment",
            "database",
            [
                .. ReportedSettings.Select(name => (name, databaseSettings[name])),
                ("nanoCpus", databaseHostConfiguration.NanoCPUs.ToString(CultureInfo.InvariantCulture)),
                ("memoryBytes", databaseHostConfiguration.Memory.ToString(CultureInfo.InvariantCulture)),
            ]
        );
        Assert.All(DurableSettings, name => Assert.Equal("on", databaseSettings[name]));
        Assert.Equal(("35", "0"), (databaseSettings["max_connections"], databaseSettings["superuser_reserved_connections"]));
        Assert.Equal((DatabaseNanoCpus, DatabaseMemoryBytes), (databaseHostConfiguration.NanoCPUs, databaseHostConfiguration.Memory));

        api = new ContainerBuilder(ApiImage)
            .WithBindMount(publishDirectory, ApiDirectory, AccessMode.ReadOnly)
            .WithWorkingDirectory(ApiDirectory)
            .WithEntrypoint("dotnet", "Murmli.Backend.Api.Host.dll")
            .WithEnvironment(CreateApiEnvironment())
            .WithNetwork(network)
            .WithPortBinding(ApiPort, assignRandomHostPort: true)
            .WithCreateParameterModifier(parameters => LimitResources(parameters, ApiNanoCpus, ApiMemoryBytes))
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPort(ApiPort).ForPath("/health"), strategy => strategy.WithTimeout(TimeSpan.FromMinutes(2)))
            )
            .Build();
        try
        {
            await api.StartAsync(cancellationToken);
        }
        catch
        {
            if (api.State is not TestcontainersStates.Undefined)
            {
                await WriteApiLogsAsync();
            }

            throw;
        }

        httpClient = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://{api.Hostname}:{api.GetMappedPublicPort(ApiPort)}"),
            Timeout = TimeSpan.FromMinutes(5),
        };
        var inspection = await docker.Containers.InspectContainerAsync(api.Id, cancellationToken);
        var apiHostConfiguration = inspection.HostConfig ?? throw new InvalidOperationException("Docker returned no host config.");
        SyncLoadReport.WriteText(
            "environment",
            "api",
            ("image", ApiImage),
            ("runtime", inspection.Config?.Env?.SingleOrDefault(variable => variable.StartsWith(RuntimeVersionVariable, StringComparison.Ordinal))?[RuntimeVersionVariable.Length..] ?? "unknown"),
            ("nanoCpus", apiHostConfiguration.NanoCPUs.ToString(CultureInfo.InvariantCulture)),
            ("memoryBytes", apiHostConfiguration.Memory.ToString(CultureInfo.InvariantCulture)),
            ("memorySwapBytes", apiHostConfiguration.MemorySwap.ToString(CultureInfo.InvariantCulture))
        );
        SyncLoadReport.Write(
            "environment",
            "settings",
            ("seed", settings.Seed),
            ("lists", settings.Lists),
            ("itemsPerList", settings.ItemsPerList),
            ("queueCommands", settings.QueueCommands),
            ("settleSeconds", settings.Settle.TotalSeconds),
            ("windowLimitSeconds", settings.Window.TotalSeconds)
        );
        Assert.Equal((ApiNanoCpus, ApiMemoryBytes), (apiHostConfiguration.NanoCPUs, apiHostConfiguration.Memory));
    }

    private Dictionary<string, string> CreateApiEnvironment() =>
        new()
        {
            ["ConnectionStrings__murmli"] = new NpgsqlConnectionStringBuilder
            {
                Host = DatabaseAlias,
                Port = PostgreSqlBuilder.PostgreSqlPort,
                Database = DatabaseName,
                Username = PostgreSqlBuilder.DefaultUsername,
                Password = PostgreSqlBuilder.DefaultPassword,
                MaxPoolSize = 20,
            }.ConnectionString,
            ["Authentication__Schemes__Bearer__ValidIssuer"] = TestTokens.Issuer,
            ["Authentication__Schemes__Bearer__ValidAudiences__0"] = TestTokens.Audience,
            ["Authentication__Schemes__Bearer__SigningKeys__0__Issuer"] = TestTokens.Issuer,
            ["Authentication__Schemes__Bearer__SigningKeys__0__Value"] = TestTokens.SigningKeyBase64,
            ["AuthService__BaseAddress"] = "http://auth.test",
            ["AuthService__Key"] = ApiHost.AuthServiceKey,
            ["Sync__Cleanup__Enabled"] = "false",
            ["Sync__RateLimits__DeviceRequestsPerMinute"] = "1000000",
            ["Sync__RateLimits__AccountRequestsPerMinute"] = "1000000",
            ["Sync__RateLimits__AccountCommandsPerHour"] = "100000000",
            ["Logging__Console__LogLevel__Default"] = "Warning",
        };

    private async Task<SyncLoadAccount> CreateLargestAccountAsync()
    {
        await StartAsync();
        var account = await SyncLoadAccount.CreateAsync(HttpClient, Settings.Lists);
        var content = await SyncLoadContent.CreateAsync(Settings.Seed);
        await AssertApiSurvivesAsync(() => account.FillAsync(Settings.ItemsPerList, content));
        return account;
    }

    private async Task AssertApiRunningAsync()
    {
        var state = (await DockerEngine.Containers.InspectContainerAsync(ApiContainer.Id, TestContext.Current.CancellationToken)).State
            ?? throw new InvalidOperationException("Docker returned no container state.");
        if (state.Running && !state.OOMKilled)
        {
            return;
        }

        await WriteApiLogsAsync();
        Assert.Fail($"The API container stopped (OOMKilled {state.OOMKilled}, exit code {state.ExitCode}).");
    }

    private async Task<SyncLoadContainerReading> ReadApiContainerAsync()
    {
        var statisticsReport = new StatisticsReport();
        await DockerEngine.Containers.GetContainerStatsAsync(
            ApiContainer.Id,
            new ContainerStatsParameters { Stream = false, OneShot = true },
            statisticsReport,
            TestContext.Current.CancellationToken
        );
        var statistics = statisticsReport.Response ?? throw new InvalidOperationException("Docker returned no stats.");
        var cpu = statistics.CPUStats ?? throw new InvalidOperationException("Docker returned no CPU stats.");
        var memory = statistics.MemoryStats ?? throw new InvalidOperationException("Docker returned no memory stats.");
        var usage = memory.Usage.GetValueOrDefault();
        // Docker's own CLI leaves the reclaimable page cache out of the usage in the same way
        var used = memory.Stats is not null && memory.Stats.TryGetValue("inactive_file", out var inactiveFile) && inactiveFile < usage ? usage - inactiveFile : usage;
        return new SyncLoadContainerReading(statistics.Read, cpu.CPUUsage.TotalUsage, used, memory.Limit.GetValueOrDefault());
    }

    private async Task WriteApiLogsAsync()
    {
        var (output, error) = await ApiContainer.GetLogsAsync(ct: TestContext.Current.CancellationToken);
        Console.WriteLine(output);
        Console.WriteLine(error);
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadDatabaseSettingsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenDatabaseConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT name, setting FROM pg_settings WHERE name = ANY(@names)", connection);
        command.Parameters.AddWithValue("names", ReportedSettings);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new Dictionary<string, string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0), reader.GetString(1));
        }

        return values;
    }

    private async Task<NpgsqlConnection> OpenDatabaseConnectionAsync()
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(DatabaseContainer.GetConnectionString()) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private sealed class StatisticsReport : IProgress<ContainerStatsResponse>
    {
        public ContainerStatsResponse? Response { get; private set; }

        public void Report(ContainerStatsResponse value) => Response = value;
    }
}
