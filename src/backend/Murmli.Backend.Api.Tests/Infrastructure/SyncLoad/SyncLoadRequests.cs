using System.Net;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal sealed class SyncLoadRequests
{
    private readonly List<TimeSpan> durations = [];

    private readonly List<HttpStatusCode> statuses = [];

    private readonly List<int> bodyLengths = [];

    private SyncLoadExchange? first;

    public SyncLoadExchange First => first ?? throw new InvalidOperationException("No request ran in the window.");

    public int Count => durations.Count;

    public IReadOnlyList<TimeSpan> Durations => durations;

    public void Add(SyncLoadExchange exchange)
    {
        first ??= exchange;
        durations.Add(exchange.Duration);
        statuses.Add(exchange.Status);
        bodyLengths.Add(exchange.Body.Length);
    }

    public void AssertAllOkWithSameBodyLength()
    {
        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.All(bodyLengths, length => Assert.Equal(First.Body.Length, length));
    }
}
