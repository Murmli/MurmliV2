using System.Net;
using System.Text;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal sealed record SyncLoadExchange(HttpStatusCode Status, TimeSpan Duration, int WireBytes, byte[] Body, bool Compressed)
{
    public SyncResponse ReadResponse()
    {
        Assert.True(Status == HttpStatusCode.OK, $"The sync answered {(int)Status}: {Encoding.UTF8.GetString(Body)}");
        return SyncTestDevice.DeserializeResponse(Encoding.UTF8.GetString(Body));
    }
}
