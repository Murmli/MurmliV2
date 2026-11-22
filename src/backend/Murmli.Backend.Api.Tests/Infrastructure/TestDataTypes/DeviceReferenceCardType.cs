using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class DeviceReferenceCardType : SyncDataType<TestCard>
{
    protected override void Define(SyncDataTypeBuilder<TestCard> type)
    {
        type.Named("deviceReferenceCard", TestBoardType.ScopeKindName).MaxRecordsPerScope(1);
        type.Field("changedBy", card => card.ChangedByMemberId, card => card.TextChangedAt, SyncFieldTypes.MemberReference());
    }
}
