using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class UnfilledCreatedAtCardType : SyncDataType<TestCard>
{
    protected override void Define(SyncDataTypeBuilder<TestCard> type)
    {
        type.Named("unfilledCard", TestBoardType.ScopeKindName).MaxRecordsPerScope(1);
        type.Field("text", card => card.Text, card => card.TextChangedAt, SyncFieldTypes.Text(0, 100));
        type.ServerField("createdAt", card => card.CreatedAt, SyncFieldTypes.Timestamp());
    }
}
