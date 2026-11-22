namespace Murmli.Backend.Sync.DataTypes;

internal readonly record struct SyncCommandKind(string Key)
{
    public static SyncCommandKind Create { get; } = new("create");

    public static SyncCommandKind SetFields { get; } = new("setFields");

    public static SyncCommandKind Delete { get; } = new("delete");

    private static readonly SyncCommandKind[] Kinds = [Create, SetFields, Delete];

    public static bool TryParse(string key, out SyncCommandKind kind)
    {
        kind = Array.Find(Kinds, candidate => candidate.Key == key);
        return kind != default;
    }
}
