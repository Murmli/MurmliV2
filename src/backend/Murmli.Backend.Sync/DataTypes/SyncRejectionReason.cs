namespace Murmli.Backend.Sync.DataTypes;

public readonly record struct SyncRejectionReason(string Key)
{
    public static SyncRejectionReason NoAccess { get; } = new("noAccess");

    public static SyncRejectionReason LimitExceeded { get; } = new("limitExceeded");

    public static SyncRejectionReason Invalid { get; } = new("invalid");

    public static SyncRejectionReason ClockAhead { get; } = new("clockAhead");

    public static SyncRejectionReason TooOld { get; } = new("tooOld");
}
