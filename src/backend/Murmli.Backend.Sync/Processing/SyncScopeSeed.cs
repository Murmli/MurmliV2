using System.Security.Cryptography;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Processing;

internal sealed record SyncScopeSeed(Guid OwnerMemberId, int Epoch)
{
    public static SyncScopeSeed? For(SyncCommand? command) =>
        command is { DataType.IsScopeRoot: true } && command.Kind == SyncCommandKind.Create
            ? new SyncScopeSeed(Guid.NewGuid(), RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue))
            : null;
}
