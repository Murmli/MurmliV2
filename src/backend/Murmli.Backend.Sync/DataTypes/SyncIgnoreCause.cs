namespace Murmli.Backend.Sync.DataTypes;

public readonly record struct SyncIgnoreCause(string Key)
{
    public static SyncIgnoreCause LostToNewer { get; } = new("lostToNewer");

    public static SyncIgnoreCause TargetDeleted { get; } = new("targetDeleted");
}
