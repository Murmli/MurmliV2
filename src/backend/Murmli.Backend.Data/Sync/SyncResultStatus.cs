using Thinktecture;

namespace Murmli.Backend.Data.Sync;

[SmartEnum<string>]
public partial class SyncResultStatus
{
    public static readonly SyncResultStatus Applied = new("applied");

    public static readonly SyncResultStatus Ignored = new("ignored");

    public static readonly SyncResultStatus Rejected = new("rejected");

    public static readonly SyncResultStatus AlreadyProcessed = new("alreadyProcessed");

    public static readonly SyncResultStatus Failed = new("failed");

    public static readonly SyncResultStatus Accepted = new("accepted");
}
