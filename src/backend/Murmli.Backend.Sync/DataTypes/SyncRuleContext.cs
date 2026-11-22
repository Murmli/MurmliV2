using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

public sealed class SyncRuleContext<TRecord>
    where TRecord : SyncRecord
{
    internal SyncRuleContext(TRecord record) => Record = record;

    public TRecord Record { get; }
}
