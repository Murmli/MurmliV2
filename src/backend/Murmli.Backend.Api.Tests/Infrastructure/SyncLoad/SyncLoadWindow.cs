namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal sealed record SyncLoadWindow(int Units, SyncLoadContainerReading Start, SyncLoadContainerReading End, double CpuQuota)
{
    public TimeSpan Elapsed => End.Read - Start.Read;

    public double CpuMilliseconds => (End.CpuNanoseconds - Start.CpuNanoseconds) / 1_000_000d;

    public double CpuShare => CpuMilliseconds / Elapsed.TotalMilliseconds / CpuQuota;
}
