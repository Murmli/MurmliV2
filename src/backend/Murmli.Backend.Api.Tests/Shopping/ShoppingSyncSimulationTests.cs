using System.Diagnostics;
using System.Runtime.CompilerServices;
using Murmli.Backend.Api.Tests.Infrastructure;

namespace Murmli.Backend.Api.Tests.Shopping;

public class ShoppingSyncSimulationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const int FailureOutputLineCount = 200;

    [Fact(Explicit = true)]
    public async Task SyncAsync_RandomizedDeviceSimulation_Converges()
    {
        await using var api = await ApiHost.StartAsync(
            postgres.CreateDatabaseConnectionString(),
            new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Warning" }
        );
        using var simulation = new Process
        {
            StartInfo = new ProcessStartInfo("fvm", ["flutter", "test", "simulation/sync_convergence_test.dart", "--reporter", "expanded"])
            {
                WorkingDirectory = GetAppDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment =
                {
                    ["SYNC_SIMULATION_API_URL"] = api.HttpClient.BaseAddress!.AbsoluteUri,
                    ["SYNC_SIMULATION_TOKEN_SIGNING_KEY"] = TestTokens.SigningKeyBase64,
                    ["SYNC_SIMULATION_TOKEN_ISSUER"] = TestTokens.Issuer,
                    ["SYNC_SIMULATION_TOKEN_AUDIENCE"] = TestTokens.Audience,
                },
            },
        };
        var outputHelper = TestContext.Current.TestOutputHelper;
        var outputTail = new Queue<string>();
        var outputTailLock = new Lock();
        void Forward(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data is not { } line)
            {
                return;
            }

            outputHelper?.WriteLine(line);
            lock (outputTailLock)
            {
                outputTail.Enqueue(line);
                if (outputTail.Count > FailureOutputLineCount)
                {
                    outputTail.Dequeue();
                }
            }
        }

        simulation.OutputDataReceived += Forward;
        simulation.ErrorDataReceived += Forward;
        simulation.Start();
        simulation.BeginOutputReadLine();
        simulation.BeginErrorReadLine();
        using (TestContext.Current.CancellationToken.Register(() => simulation.Kill(entireProcessTree: true)))
        {
            // Waiting without the token also drains the output of a killed simulation, later lines would reach a finished test
            await simulation.WaitForExitAsync(CancellationToken.None);
        }

        Assert.True(
            simulation.ExitCode == 0,
            $"The simulation exited with code {simulation.ExitCode}:{Environment.NewLine}{string.Join(Environment.NewLine, outputTail)}"
        );
    }

    private static string GetAppDirectory([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", "..", "app"));
}
