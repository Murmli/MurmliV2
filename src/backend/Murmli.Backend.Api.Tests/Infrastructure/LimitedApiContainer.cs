using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed class LimitedApiContainer : IAsyncDisposable
{
    private const int ApiPort = 8080;

    private const string PostgresAlias = "postgres";

    private readonly DirectoryInfo publishDirectory;

    private readonly INetwork network;

    private readonly PostgreSqlContainer postgres;

    private readonly IContainer api;

    private LimitedApiContainer(DirectoryInfo publishDirectory, INetwork network, PostgreSqlContainer postgres, IContainer api)
    {
        this.publishDirectory = publishDirectory;
        this.network = network;
        this.postgres = postgres;
        this.api = api;
    }

    public Uri BaseAddress => new UriBuilder(Uri.UriSchemeHttp, api.Hostname, api.GetMappedPublicPort(ApiPort)).Uri;

    public static async Task<LimitedApiContainer> StartAsync(
        double cpuCount = 0.25,
        long memoryBytes = 512L * 1024 * 1024,
        int? postgresMaximumConnections = null,
        int? maximumPoolSize = null,
        CancellationToken cancellationToken = default
    )
    {
        var publishDirectory = Directory.CreateTempSubdirectory("murmli-api-");
        try
        {
            await PublishApiAsync(publishDirectory.FullName, cancellationToken);
        }
        catch
        {
            publishDirectory.Delete(recursive: true);
            throw;
        }

        var network = new NetworkBuilder().Build();
        var postgresBuilder = new PostgreSqlBuilder("postgres:18").WithNetwork(network).WithNetworkAliases(PostgresAlias);
        if (postgresMaximumConnections is { } maximumConnections)
        {
            postgresBuilder = postgresBuilder.WithCommand("-c", $"max_connections={maximumConnections}");
        }

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = PostgresAlias,
            Port = PostgreSqlBuilder.PostgreSqlPort,
            Database = "murmli",
            Username = PostgreSqlBuilder.DefaultUsername,
            Password = PostgreSqlBuilder.DefaultPassword,
        };
        if (maximumPoolSize is { } poolSize)
        {
            connectionString.MaxPoolSize = poolSize;
        }

        var api = new ContainerBuilder("mcr.microsoft.com/dotnet/aspnet:11.0")
            .WithNetwork(network)
            .WithResourceMapping(publishDirectory, "/app")
            .WithWorkingDirectory("/app")
            .WithCommand("dotnet", "Murmli.Backend.Api.Host.dll")
            .WithEnvironment(
                new Dictionary<string, string>
                {
                    ["ConnectionStrings__murmli"] = connectionString.ConnectionString,
                    ["Authentication__Schemes__Bearer__ValidIssuer"] = TestTokens.Issuer,
                    ["Authentication__Schemes__Bearer__ValidAudiences__0"] = TestTokens.Audience,
                    ["Authentication__Schemes__Bearer__SigningKeys__0__Issuer"] = TestTokens.Issuer,
                    ["Authentication__Schemes__Bearer__SigningKeys__0__Value"] = TestTokens.SigningKeyBase64,
                    ["AuthService__BaseAddress"] = "http://auth.test",
                    ["AuthService__Key"] = ApiHost.AuthServiceKey,
                    ["Logging__LogLevel__Default"] = "Warning",
                }
            )
            .WithPortBinding(ApiPort, true)
            .WithCreateParameterModifier(parameters =>
            {
                parameters.HostConfig!.NanoCPUs = (long)(cpuCount * 1_000_000_000);
                parameters.HostConfig.Memory = memoryBytes;
                // Without this Docker allows as much swap as memory on hosts with swap, Azure Container Apps has none
                parameters.HostConfig.MemorySwap = memoryBytes;
            })
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(
                        request => request.ForPort(ApiPort).ForPath("/health"),
                        waitStrategy => waitStrategy.WithTimeout(TimeSpan.FromMinutes(2))
                    )
            )
            .Build();

        var container = new LimitedApiContainer(publishDirectory, network, postgresBuilder.Build(), api);
        try
        {
            await container.postgres.StartAsync(cancellationToken);
            await container.api.StartAsync(cancellationToken);
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public async Task<ResourceUsage> GetResourceUsageAsync(CancellationToken cancellationToken = default)
    {
        var result = await api.ExecAsync(
            [
                "cat",
                "/sys/fs/cgroup/memory.current",
                "/sys/fs/cgroup/memory.peak",
                "/sys/fs/cgroup/cpu.stat",
                "/sys/fs/cgroup/memory.events",
            ],
            cancellationToken
        );
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Reading the cgroup files failed with exit code {result.ExitCode}: {result.Stderr}");
        }

        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var counters = lines[2..].Select(line => line.Split(' ')).ToDictionary(fields => fields[0], fields => long.Parse(fields[1]));
        return new ResourceUsage(
            long.Parse(lines[0]),
            long.Parse(lines[1]),
            counters["usage_usec"],
            counters["throttled_usec"],
            counters["oom_kill"]
        );
    }

    public async Task<(string Api, string Postgres)> GetLogsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        var apiLogs = await api.GetLogsAsync(since, ct: cancellationToken);
        var postgresLogs = await postgres.GetLogsAsync(since, ct: cancellationToken);
        return (KeepLinesSince(apiLogs.Stdout + apiLogs.Stderr, since), KeepLinesSince(postgresLogs.Stdout + postgresLogs.Stderr, since));
    }

    public Task<long> GetApiExitCodeAsync(CancellationToken cancellationToken = default) => api.GetExitCodeAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await api.DisposeAsync();
        await postgres.DisposeAsync();
        await network.DisposeAsync();
        publishDirectory.Delete(recursive: true);
    }

    private static async Task PublishApiAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        using var publish = new Process
        {
            StartInfo = new ProcessStartInfo(
                "dotnet",
                ["publish", "Murmli.Backend.Api.Host/Murmli.Backend.Api.Host.csproj", "-c", "Release", "-p:UseAppHost=false", "-o", outputDirectory]
            )
            {
                WorkingDirectory = GetBackendDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        publish.Start();
        var output = publish.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = publish.StandardError.ReadToEndAsync(CancellationToken.None);
        using (cancellationToken.Register(() => publish.Kill(entireProcessTree: true)))
        {
            await publish.WaitForExitAsync(CancellationToken.None);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (publish.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Publishing the API failed with exit code {publish.ExitCode}:{Environment.NewLine}{await output}{await error}"
            );
        }
    }

    // Testcontainers applies since in whole seconds, the timestamp in front of every line filters the rest
    private static string KeepLinesSince(string timestampedLog, DateTime since) =>
        string.Join(
            '\n',
            timestampedLog
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(' ', 2))
                .Where(parts => DateTime.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) >= since)
                .Select(parts => parts.Length == 2 ? parts[1] : "")
        );

    private static string GetBackendDirectory([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

    public sealed record ResourceUsage(
        long MemoryCurrentBytes,
        long MemoryPeakBytes,
        long CpuUsageMicroseconds,
        long ThrottledMicroseconds,
        long OomKillCount
    );
}
