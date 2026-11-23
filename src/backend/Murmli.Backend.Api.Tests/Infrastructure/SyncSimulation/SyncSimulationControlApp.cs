using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Murmli.Backend.Sync.Cleanup;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;

internal sealed partial class SyncSimulationControlApp : IAsyncDisposable
{
    private static readonly long LatestUnixMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    private readonly WebApplication application;

    private SyncSimulationControlApp(WebApplication application, HttpClient httpClient)
    {
        this.application = application;
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }

    public string Url => application.Urls.Single();

    public static async Task<SyncSimulationControlApp> StartAsync(
        IReadOnlyList<string> apiUrls,
        FakeTimeProvider timeProvider,
        DatabaseAdministration administration,
        SyncCleanupRunner cleanupRunner
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var application = builder.Build();
        var clockGate = new Lock();
        application.MapGet(
            "/environment",
            async () =>
                TypedResults.Ok(
                    new SyncSimulationEnvironmentResponse(
                        apiUrls,
                        timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                        await administration.ReadDatabaseIdentityAsync()
                    )
                )
        );
        application.MapPost(
            "/tokens",
            Results<Ok<SyncSimulationTokenResponse>, BadRequest> (SyncSimulationTokenRequest request) =>
                string.IsNullOrWhiteSpace(request.Subject)
                    ? TypedResults.BadRequest()
                    : TypedResults.Ok(new SyncSimulationTokenResponse(TestTokens.Create(request.Subject, issuedAt: timeProvider.GetUtcNow())))
        );
        application.MapPost(
            "/clock",
            Results<Ok<SyncSimulationClockResponse>, BadRequest> (SyncSimulationClockRequest request) =>
            {
                lock (clockGate)
                {
                    var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                    long target;
                    switch (request)
                    {
                        case { AdvanceMilliseconds: { } advance, SetUnixMilliseconds: null } when advance >= -now && advance <= LatestUnixMilliseconds - now:
                            target = now + advance;
                            break;
                        case { AdvanceMilliseconds: null, SetUnixMilliseconds: { } unixMilliseconds } when unixMilliseconds >= 0 && unixMilliseconds <= LatestUnixMilliseconds:
                            target = unixMilliseconds;
                            break;
                        default:
                            return TypedResults.BadRequest();
                    }

                    var targetTime = DateTimeOffset.FromUnixTimeMilliseconds(target);
                    if (target >= now)
                    {
                        timeProvider.SetUtcNow(targetTime);
                    }
                    else
                    {
                        timeProvider.AdjustTime(targetTime);
                    }

                    return TypedResults.Ok(new SyncSimulationClockResponse(target));
                }
            }
        );
        application.MapPost(
            "/cleanup",
            async Task<NoContent> (CancellationToken cancellationToken) =>
            {
                await cleanupRunner.RunAsync(cancellationToken);
                return TypedResults.NoContent();
            }
        );
        application.MapPost(
            "/snapshots/{name}",
            async Task<Results<NoContent, BadRequest>> (string name) =>
            {
                if (!SnapshotName().IsMatch(name))
                {
                    return TypedResults.BadRequest();
                }

                await administration.CreateSnapshotAsync(name);
                return TypedResults.NoContent();
            }
        );
        application.MapPost(
            "/snapshots/{name}/restore",
            async Task<Results<NoContent, BadRequest, NotFound>> (string name) =>
            {
                if (!SnapshotName().IsMatch(name))
                {
                    return TypedResults.BadRequest();
                }

                return await administration.RestoreSnapshotAsync(name) ? TypedResults.NoContent() : TypedResults.NotFound();
            }
        );
        await application.StartAsync(TestContext.Current.CancellationToken);
        return new SyncSimulationControlApp(application, new HttpClient { BaseAddress = new Uri(application.Urls.Single()) });
    }

    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();
        await application.StopAsync(TestContext.Current.CancellationToken);
        await application.DisposeAsync();
    }

    [GeneratedRegex(@"\A[a-z0-9_]{1,25}\z")]
    private static partial Regex SnapshotName();
}
