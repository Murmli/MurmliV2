using System.Globalization;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal sealed record SyncLoadSettings(int Seed, int Lists, int ItemsPerList, int QueueCommands, TimeSpan Settle, TimeSpan Window)
{
    public const int MaxLists = 42;

    public const int MaxItemsPerList = 1_000;

    public static SyncLoadSettings FromEnvironment() =>
        new(
            Read("SYNC_LOAD_SEED", 137, 0, int.MaxValue),
            Read("SYNC_LOAD_LISTS", MaxLists, 1, MaxLists),
            Read("SYNC_LOAD_ITEMS_PER_LIST", MaxItemsPerList, 1, MaxItemsPerList),
            Read("SYNC_LOAD_QUEUE_COMMANDS", 5_000, 501, MaxLists * MaxItemsPerList),
            TimeSpan.FromSeconds(Read("SYNC_LOAD_SETTLE_SECONDS", 30, 0, 3_600)),
            TimeSpan.FromSeconds(Read("SYNC_LOAD_WINDOW_SECONDS", 60, 1, 3_600))
        );

    private static int Read(string name, int defaultValue, int minimum, int maximum)
    {
        var text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(text))
        {
            return defaultValue;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{name} must be a whole number from {minimum} to {maximum}.");
        }

        return value;
    }
}
