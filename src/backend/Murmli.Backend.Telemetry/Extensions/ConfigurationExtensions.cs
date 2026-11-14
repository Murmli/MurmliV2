using Microsoft.Extensions.Configuration;

namespace Murmli.Backend.Telemetry.Extensions;

public static class ConfigurationExtensions
{
    private const string RecordSensitiveDataKey = "Telemetry:RecordSensitiveData";

    public static bool RecordsSensitiveData(this IConfiguration configuration) => configuration.GetValue<bool>(RecordSensitiveDataKey);
}
