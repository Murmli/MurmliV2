using System.Diagnostics;

namespace Murmli.Backend.Telemetry;

internal sealed class TraceContextOnlyPropagator : DistributedContextPropagator
{
    private const string BaggageField = "baggage";

    private static readonly DistributedContextPropagator TraceContextPropagator = CreateW3CPropagator();

    private static readonly IReadOnlyCollection<string> TraceContextFields = [.. TraceContextPropagator.Fields.Where(field => !IsBaggage(field))];

    public override IReadOnlyCollection<string> Fields => TraceContextFields;

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
    {
        if (setter is null)
        {
            return;
        }

        TraceContextPropagator.Inject(
            activity,
            carrier,
            (innerCarrier, name, value) =>
            {
                if (!IsBaggage(name))
                {
                    setter(innerCarrier, name, value);
                }
            }
        );
    }

    public override void ExtractTraceIdAndState(
        object? carrier,
        PropagatorGetterCallback? getter,
        out string? traceId,
        out string? traceState
    ) => TraceContextPropagator.ExtractTraceIdAndState(carrier, getter, out traceId, out traceState);

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) => null;

    private static bool IsBaggage(string field) => string.Equals(field, BaggageField, StringComparison.OrdinalIgnoreCase);
}
