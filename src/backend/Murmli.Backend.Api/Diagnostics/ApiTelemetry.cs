using System.Diagnostics.Metrics;

namespace Murmli.Backend.Api.Diagnostics;

internal sealed class ApiTelemetry
{
    private const string MeterName = "Murmli.Backend.Api";

    private readonly Counter<long> rejectedSyncRequests;

    public ApiTelemetry(IMeterFactory meterFactory) =>
        rejectedSyncRequests = meterFactory.Create(MeterName).CreateCounter<long>("murmli.sync.requests.rejected", "{request}");

    public void RecordSyncRequestRejected(string rejection) =>
        rejectedSyncRequests.Add(1, new KeyValuePair<string, object?>("murmli.sync.rejection.reason", rejection));
}
