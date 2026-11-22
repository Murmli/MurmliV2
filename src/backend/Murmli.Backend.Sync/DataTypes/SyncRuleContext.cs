using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

public sealed class SyncRuleContext<TRecord>
    where TRecord : SyncRecord
{
    internal SyncRuleContext(TRecord record, Guid? actingMemberId)
    {
        Record = record;
        ActingMemberId = actingMemberId;
    }

    public TRecord Record { get; }

    public Guid? ActingMemberId { get; }
}
