using Microsoft.Extensions.Configuration;

namespace Murmli.Backend.Aspire.Telemetry;

public static class Extensions
{
    public static IResourceBuilder<T> WithTelemetry<T>(this IResourceBuilder<T> resource)
        where T : IResourceWithEnvironment =>
        resource.ApplicationBuilder.Configuration.GetValue<bool>("Telemetry:RecordSensitiveData")
            ? resource.WithEnvironment("Telemetry__RecordSensitiveData", "true")
            : resource;
}
