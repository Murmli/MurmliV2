using System.Threading.Channels;

namespace Murmli.Backend.Api.Shopping;

internal sealed class SyncEventSubscription(ChannelReader<bool> pokes, Action unsubscribe) : IDisposable
{
    public ChannelReader<bool> Pokes { get; } = pokes;

    public void Dispose() => unsubscribe();
}
