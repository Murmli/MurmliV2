using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Murmli.Backend.Telemetry.Extensions;

public static class HostApplicationBuilderExtensions
{
    private const string CaptureMessageContentVariable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";

    public static void AddTelemetry(this IHostApplicationBuilder builder)
    {
        var captureMessageContent = builder.Configuration.RecordsSensitiveData() ? "true" : "false";
        // Microsoft.Extensions.AI reads the process variable once at startup, other libraries read the configuration
        Environment.SetEnvironmentVariable(CaptureMessageContentVariable, captureMessageContent);
        builder.Configuration[CaptureMessageContentVariable] = captureMessageContent;

        // HttpClient handlers capture the global propagator, ASP.NET Core resolves its own from DI
        var propagator = new TraceContextOnlyPropagator();
        DistributedContextPropagator.Current = propagator;
        builder.Services.AddSingleton<DistributedContextPropagator>(propagator);

        builder.Logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.None);
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var openTelemetry = builder
            .Services.AddOpenTelemetry()
            .WithTracing(tracing =>
                // Ignores the caller's sampled flag on purpose, so every request is findable by its trace ID
                tracing.SetSampler(new AlwaysOnSampler()).AddSource("Microsoft.AspNetCore", "System.Net.Http", "Murmli.*")
            )
            .WithMetrics(metrics =>
                metrics.AddMeter("Microsoft.AspNetCore.*", "System.Net.Http", "System.Net.NameResolution", "System.Runtime", "Murmli.*")
            );

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }
    }
}
