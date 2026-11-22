namespace Murmli.Backend.Sync.Extensions;

internal static class TimeProviderExtensions
{
    public static DateTimeOffset GetUtcNowInMilliseconds(this TimeProvider timeProvider) =>
        DateTimeOffset.FromUnixTimeMilliseconds(timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
}
