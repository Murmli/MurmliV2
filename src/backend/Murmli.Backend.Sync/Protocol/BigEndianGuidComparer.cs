namespace Murmli.Backend.Sync.Protocol;

internal sealed class BigEndianGuidComparer : IComparer<Guid>
{
    public static BigEndianGuidComparer Instance { get; } = new();

    public int Compare(Guid left, Guid right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        left.TryWriteBytes(leftBytes, bigEndian: true, out _);
        right.TryWriteBytes(rightBytes, bigEndian: true, out _);
        return leftBytes.SequenceCompareTo(rightBytes);
    }
}
