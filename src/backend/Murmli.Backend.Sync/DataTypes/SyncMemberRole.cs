namespace Murmli.Backend.Sync.DataTypes;

public readonly record struct SyncMemberRole(string Key)
{
    public static SyncMemberRole Owner { get; } = new("owner");

    public static SyncMemberRole Member { get; } = new("member");

    internal static IReadOnlyList<SyncMemberRole> Known { get; } = [Owner, Member];

    internal bool Satisfies(SyncMemberRole minimum) => Rank(minimum) > 0 && Rank(this) >= Rank(minimum);

    private static int Rank(SyncMemberRole role) =>
        role == Owner ? 2
        : role == Member ? 1
        : 0;
}
