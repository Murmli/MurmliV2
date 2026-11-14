using Aspire.Hosting.Azure;
using Microsoft.Extensions.Configuration;

namespace Murmli.Backend.Aspire.Telemetry;

public static class Extensions
{
    public static IResourceBuilder<T> WithTelemetry<T>(
        this IResourceBuilder<T> resource,
        IResourceBuilder<AzureApplicationInsightsResource>? applicationInsights
    )
        where T : IResourceWithEnvironment
    {
        if (resource.ApplicationBuilder.Configuration.GetValue<bool>("Telemetry:RecordSensitiveData"))
        {
            resource.WithEnvironment("Telemetry__RecordSensitiveData", "true");
        }

        if (applicationInsights is not null)
        {
            resource
                .WithReference(applicationInsights)
                .WithEnvironment("OTEL_SERVICE_NAME", resource.Resource.Name)
                .WithEnvironment("Logging__Console__LogLevel__Default", "Warning");
        }

        return resource;
    }
}
