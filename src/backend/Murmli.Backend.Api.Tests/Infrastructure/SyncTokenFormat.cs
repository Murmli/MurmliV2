using System.Buffers.Binary;
using System.Buffers.Text;

namespace Murmli.Backend.Api.Tests.Infrastructure;

internal static class SyncTokenFormat
{
    private const int IdLength = 16;

    private const int HeaderLength = 1 + IdLength;

    private const int ScopeLength = IdLength + sizeof(int) + sizeof(long);

    public static (Guid DatabaseIdentity, IReadOnlyList<(Guid ScopeId, int Epoch, long Version)> Scopes) Decode(string token)
    {
        var bytes = Base64Url.DecodeFromChars(token);
        Assert.True(bytes.Length >= HeaderLength, $"The token has only {bytes.Length} bytes.");
        Assert.Equal(1, bytes[0]);
        Assert.Equal(0, (bytes.Length - HeaderLength) % ScopeLength);

        var scopes = new List<(Guid ScopeId, int Epoch, long Version)>();
        for (var offset = HeaderLength; offset < bytes.Length; offset += ScopeLength)
        {
            var entry = bytes.AsSpan(offset, ScopeLength);
            scopes.Add(
                (
                    new Guid(entry[..IdLength], bigEndian: true),
                    BinaryPrimitives.ReadInt32BigEndian(entry[IdLength..]),
                    BinaryPrimitives.ReadInt64BigEndian(entry[(IdLength + sizeof(int))..])
                )
            );
        }

        return (new Guid(bytes.AsSpan(1, IdLength), bigEndian: true), scopes);
    }

    public static string Encode(
        Guid databaseIdentity,
        IReadOnlyList<(Guid ScopeId, int Epoch, long Version)> scopes,
        byte format = 1
    )
    {
        var bytes = new byte[HeaderLength + ScopeLength * scopes.Count];
        bytes[0] = format;
        databaseIdentity.TryWriteBytes(bytes.AsSpan(1, IdLength), bigEndian: true, out _);
        foreach (var (index, scope) in scopes.Index())
        {
            var entry = bytes.AsSpan(HeaderLength + ScopeLength * index, ScopeLength);
            scope.ScopeId.TryWriteBytes(entry[..IdLength], bigEndian: true, out _);
            BinaryPrimitives.WriteInt32BigEndian(entry[IdLength..], scope.Epoch);
            BinaryPrimitives.WriteInt64BigEndian(entry[(IdLength + sizeof(int))..], scope.Version);
        }

        return Base64Url.EncodeToString(bytes);
    }
}
