namespace Murmli.Backend.Sync.Diagnostics;

internal static class SyncFullStateReason
{
    public const string NoToken = "no_token";

    public const string UnreadableToken = "unreadable_token";

    public const string DatabaseIdentity = "database_identity";

    public const string RootDeleteNotApplied = "root_delete_not_applied";

    public const string UnknownScope = "unknown_scope";

    public const string Epoch = "epoch";

    public const string BelowFloor = "below_floor";

    public const string AheadOfServer = "ahead_of_server";

    public const string TooManyDeletions = "too_many_deletions";
}
