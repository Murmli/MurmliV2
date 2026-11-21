using System.Threading.Channels;

namespace Murmli.Backend.Api.Shopping;

internal sealed class SyncEventHub
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, HashSet<Channel<bool>>> subscriptionsByUser = [];

    public SyncEventSubscription Subscribe(Guid userId)
    {
        // Capacity one with DropWrite coalesces pokes that arrive while the stream is still writing
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        lock (gate)
        {
            if (!subscriptionsByUser.TryGetValue(userId, out var subscriptions))
            {
                subscriptions = [];
                subscriptionsByUser[userId] = subscriptions;
            }

            subscriptions.Add(channel);
        }

        return new SyncEventSubscription(channel.Reader, () => Unsubscribe(userId, channel));
    }

    public void Poke(IEnumerable<Guid> userIds)
    {
        lock (gate)
        {
            foreach (var userId in userIds)
            {
                if (subscriptionsByUser.TryGetValue(userId, out var subscriptions))
                {
                    foreach (var subscription in subscriptions)
                    {
                        subscription.Writer.TryWrite(true);
                    }
                }
            }
        }
    }

    public void PokeEveryone()
    {
        lock (gate)
        {
            foreach (var subscription in subscriptionsByUser.Values.SelectMany(subscriptions => subscriptions))
            {
                subscription.Writer.TryWrite(true);
            }
        }
    }

    private void Unsubscribe(Guid userId, Channel<bool> channel)
    {
        lock (gate)
        {
            if (subscriptionsByUser.TryGetValue(userId, out var subscriptions) && subscriptions.Remove(channel) && subscriptions.Count == 0)
            {
                subscriptionsByUser.Remove(userId);
            }
        }
    }
}
