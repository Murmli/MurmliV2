using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Telemetry.Extensions;
using Murmli.Backend.Telemetry.Tests.Infrastructure;
using OpenTelemetry.Trace;

namespace Murmli.Backend.Telemetry.Tests.Extensions;

public class HostApplicationBuilderExtensionsTests
{
    private const string IncomingTraceId = "0af7651916cd43dd8448eb211c80319c";

    private const string IncomingSpanId = "b7ad6b7169203331";

    private const string CaptureMessageContentVariable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";

    // Refuses every connection, a local application as target would record its own server span under the caller's trace ID
    private static readonly Uri AddressWithoutListener = new("http://127.0.0.1:1/");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddTelemetry_RequestWithUnsampledTraceparent_RecordsRequestUnderIncomingTraceId(bool applicationInsightsConfigured)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        List<Activity> activities = [];
        await using var ingestion = applicationInsightsConfigured ? LocalWebApplication.CreateBuilder().Build() : null;
        var builder = await CreateBuilderExportingTracesAsync(activities, ingestion);
        await using var application = builder.Build();
        application.MapGet("/", () => Results.Ok());
        var address = await LocalWebApplication.StartAsync(application);

        using var httpClient = new HttpClient { BaseAddress = address };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("traceparent", $"00-{IncomingTraceId}-{IncomingSpanId}-00");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await application.StopAsync(cancellationToken);

        var requestActivity = Assert.Single(
            activities,
            activity => activity.Source.Name == "Microsoft.AspNetCore" && activity.TraceId.ToHexString() == IncomingTraceId
        );
        if (applicationInsightsConfigured)
        {
            // The exporter's samplers tag every span they keep, a missing tag means our sampler decided
            Assert.Null(requestActivity.GetTagItem("microsoft.sample_rate"));
        }
    }

    [Theory]
    [InlineData("/health", false)]
    [InlineData("/health", true)]
    [InlineData("/Health/", false)]
    public async Task AddTelemetry_HealthRequest_RecordsNeitherRequestNorChildSpans(string path, bool applicationInsightsConfigured)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        List<Activity> activities = [];
        ActivityTraceId? healthTraceId = null;
        ActivityTraceId? contrastTraceId = null;
        await using var ingestion = applicationInsightsConfigured ? LocalWebApplication.CreateBuilder().Build() : null;
        var builder = await CreateBuilderExportingTracesAsync(activities, ingestion);
        using var outgoingHttpClient = new HttpClient();
        await using var application = builder.Build();
        application.MapGet(
            "/health",
            async (CancellationToken requestAborted) =>
            {
                healthTraceId = Activity.Current?.TraceId;
                await CallAddressWithoutListenerAsync(outgoingHttpClient, requestAborted);
            }
        );
        application.MapGet(
            "/",
            async (CancellationToken requestAborted) =>
            {
                contrastTraceId = Activity.Current?.TraceId;
                await CallAddressWithoutListenerAsync(outgoingHttpClient, requestAborted);
            }
        );
        var address = await LocalWebApplication.StartAsync(application);

        // Without propagation each request starts its own trace, the test's client span would otherwise share the health trace ID
        using var httpClient = new HttpClient(new SocketsHttpHandler { ActivityHeadersPropagator = null }) { BaseAddress = address };
        using var healthResponse = await httpClient.GetAsync(path, cancellationToken);
        healthResponse.EnsureSuccessStatusCode();
        using var contrastResponse = await httpClient.GetAsync("/", cancellationToken);
        contrastResponse.EnsureSuccessStatusCode();
        await application.StopAsync(cancellationToken);

        Assert.NotNull(healthTraceId);
        Assert.DoesNotContain(activities, activity => activity.TraceId == healthTraceId);
        var contrastRequestActivity = Assert.Single(
            activities,
            activity => activity.Source.Name == "Microsoft.AspNetCore" && activity.TraceId == contrastTraceId
        );
        Assert.Contains(
            activities,
            activity => activity.Source.Name == "System.Net.Http" && activity.ParentSpanId == contrastRequestActivity.SpanId
        );
    }

    [Fact]
    public async Task AddTelemetry_RequestWithBaggage_NeitherExtractsNorForwardsBaggage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        bool? requestArrivedWithBaggage = null;
        string? forwardedTraceParent = null;
        string? forwardedBaggage = null;
        await using var downstream = LocalWebApplication.CreateBuilder().Build();
        downstream.MapGet(
            "/",
            (HttpRequest request) =>
            {
                forwardedTraceParent = request.Headers.TraceParent;
                forwardedBaggage = request.Headers.Baggage;
            }
        );
        var downstreamAddress = await LocalWebApplication.StartAsync(downstream);

        // ASP.NET Core defaults to the global propagator at builder creation, an earlier AddTelemetry call would mask a missing DI registration
        DistributedContextPropagator.Current = DistributedContextPropagator.CreateDefaultPropagator();
        var builder = LocalWebApplication.CreateBuilder();
        builder.AddTelemetry();
        builder.Services.AddHttpClient();
        await using var application = builder.Build();
        application.MapGet(
            "/",
            async (IHttpClientFactory httpClientFactory, CancellationToken requestAborted) =>
            {
                var requestActivity = Activity.Current!;
                requestArrivedWithBaggage = requestActivity.Baggage.Any();
                requestActivity.AddBaggage("sessionId", "42");
                using var response = await httpClientFactory.CreateClient().GetAsync(downstreamAddress, requestAborted);
                response.EnsureSuccessStatusCode();
            }
        );
        var address = await LocalWebApplication.StartAsync(application);

        using var httpClient = new HttpClient { BaseAddress = address };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("traceparent", $"00-{IncomingTraceId}-{IncomingSpanId}-01");
        request.Headers.Add("baggage", "userId=alice");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        Assert.False(requestArrivedWithBaggage);
        Assert.StartsWith($"00-{IncomingTraceId}-", forwardedTraceParent);
        Assert.Null(forwardedBaggage);
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData(null, "false")]
    public void AddTelemetry_SensitiveDataSwitch_SetsGenAiContentCapture(string? recordSensitiveData, string expectedContentCapture)
    {
        var previousContentCapture = Environment.GetEnvironmentVariable(CaptureMessageContentVariable);
        Environment.SetEnvironmentVariable(CaptureMessageContentVariable, "true");
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Telemetry:RecordSensitiveData"] = recordSensitiveData }
            );

            builder.AddTelemetry();

            Assert.Equal(expectedContentCapture, Environment.GetEnvironmentVariable(CaptureMessageContentVariable));
            Assert.Equal(expectedContentCapture, builder.Configuration[CaptureMessageContentVariable]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaptureMessageContentVariable, previousContentCapture);
        }
    }

    private static async Task<WebApplicationBuilder> CreateBuilderExportingTracesAsync(ICollection<Activity> activities, WebApplication? ingestion)
    {
        var builder = LocalWebApplication.CreateBuilder();
        if (ingestion is not null)
        {
            ingestion.MapPost("/v2.1/track", () => Results.Ok());
            var ingestionAddress = await LocalWebApplication.StartAsync(ingestion);
            builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
                $"InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint={ingestionAddress}";
        }

        builder.AddTelemetry();
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(activities));
        return builder;
    }

    private static async Task CallAddressWithoutListenerAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(AddressWithoutListener, cancellationToken);
        }
        catch (HttpRequestException) { }
    }
}
