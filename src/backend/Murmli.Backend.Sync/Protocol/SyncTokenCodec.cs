using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

namespace Murmli.Backend.Sync.Protocol;

internal static class SyncTokenCodec
{
    private const byte Format = 0x01;

    private const int IdLength = 16;

    private const int HeaderLength = 1 + IdLength;

    private const int ScopeLength = IdLength + sizeof(int) + sizeof(long);

    public static string Encode(Guid databaseIdentity, IEnumerable<SyncTokenScope> scopes)
    {
        var orderedScopes = scopes.OrderBy(scope => scope.ScopeId, BigEndianGuidComparer.Instance).ToArray();

        var bytes = new byte[HeaderLength + ScopeLength * orderedScopes.Length];
        bytes[0] = Format;
        databaseIdentity.TryWriteBytes(bytes.AsSpan(1, IdLength), bigEndian: true, out _);
        foreach (var (index, scope) in orderedScopes.Index())
        {
            var entry = bytes.AsSpan(HeaderLength + ScopeLength * index, ScopeLength);
            scope.ScopeId.TryWriteBytes(entry, bigEndian: true, out _);
            BinaryPrimitives.WriteInt32BigEndian(entry[IdLength..], scope.Epoch);
            BinaryPrimitives.WriteInt64BigEndian(entry[(IdLength + sizeof(int))..], scope.Version);
        }

        return Base64Url.EncodeToString(bytes);
    }

    public static bool TryDecode(string? token, [NotNullWhen(true)] out SyncToken? decoded)
    {
        decoded = null;
        // DecodeFromChars throws on invalid characters
        if (token is null || !Base64Url.IsValid(token))
        {
            return false;
        }

        var bytes = Base64Url.DecodeFromChars(token);
        if (bytes.Length < HeaderLength || (bytes.Length - HeaderLength) % ScopeLength != 0 || bytes[0] != Format)
        {
            return false;
        }

        var scopes = new SyncTokenScope[(bytes.Length - HeaderLength) / ScopeLength];
        for (var index = 0; index < scopes.Length; index++)
        {
            var entry = bytes.AsSpan(HeaderLength + ScopeLength * index, ScopeLength);
            var scopeId = new Guid(entry[..IdLength], bigEndian: true);
            if (index > 0 && BigEndianGuidComparer.Instance.Compare(scopes[index - 1].ScopeId, scopeId) >= 0)
            {
                return false;
            }

            scopes[index] = new SyncTokenScope(
                scopeId,
                BinaryPrimitives.ReadInt32BigEndian(entry[IdLength..]),
                BinaryPrimitives.ReadInt64BigEndian(entry[(IdLength + sizeof(int))..])
            );
        }

        decoded = new SyncToken(new Guid(bytes.AsSpan(1, IdLength), bigEndian: true), scopes);
        return true;
    }
}
