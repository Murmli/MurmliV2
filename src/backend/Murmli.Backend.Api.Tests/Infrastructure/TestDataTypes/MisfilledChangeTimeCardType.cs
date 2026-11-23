using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class MisfilledChangeTimeCardType : SyncDataType<TestCard>
{
    protected override void Define(SyncDataTypeBuilder<TestCard> type)
    {
        type.Named("misfilledChangeTimeCard", TestBoardType.ScopeKindName).MaxRecordsPerScope(1);
        type.Field("text", card => card.Text, card => card.TextChangedAt, SyncFieldTypes.Text(0, 100));
        type.ServerField("changedAt", card => card.ChangedByMemberId, SyncFieldTypes.MemberReference(), SyncServerFill.CommandTimeOnApply);
    }
}
