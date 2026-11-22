namespace Murmli.Backend.Sync.DataTypes;

public readonly record struct SyncRuleDecision
{
    private SyncRuleDecision(SyncIgnoreCause? ignoreCause, SyncRejectionReason? rejectionReason)
    {
        IgnoreCause = ignoreCause;
        RejectionReason = rejectionReason;
    }

    public static SyncRuleDecision Continue => default;

    internal SyncIgnoreCause? IgnoreCause { get; }

    internal SyncRejectionReason? RejectionReason { get; }

    public static SyncRuleDecision Ignore(SyncIgnoreCause cause) => new(cause, null);

    public static SyncRuleDecision Reject(SyncRejectionReason reason) => new(null, reason);
}
