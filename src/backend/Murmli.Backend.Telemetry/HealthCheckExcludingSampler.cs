using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Murmli.Backend.Telemetry;

internal sealed class HealthCheckExcludingSampler : Sampler
{
    private const string HealthCheckPath = "/health";

    private const string UrlPathTag = "url.path";

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        new(
            samplingParameters.Kind == ActivityKind.Server && IsHealthCheckRequest(samplingParameters.Tags)
                ? SamplingDecision.Drop
                : SamplingDecision.RecordAndSample
        );

    private static bool IsHealthCheckRequest(IEnumerable<KeyValuePair<string, object?>>? tags)
    {
        if (tags is null)
        {
            return false;
        }

        foreach (var tag in tags)
        {
            if (tag.Key == UrlPathTag)
            {
                return tag.Value is string path && IsHealthCheckPath(path);
            }
        }

        return false;
    }

    private static bool IsHealthCheckPath(string path) =>
        path.Equals(HealthCheckPath, StringComparison.OrdinalIgnoreCase)
        || path.Equals(HealthCheckPath + "/", StringComparison.OrdinalIgnoreCase);
}
