using Thinktecture;

namespace Murmli.Backend.Data.Sync;

[SmartEnum<string>]
public partial class SyncMemberState
{
    public static readonly SyncMemberState Active = new("active");

    public static readonly SyncMemberState Former = new("former");
}
