using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class OptionalCreateOnlyKeyCardType : SyncDataType<TestCard>
{
    protected override void Define(SyncDataTypeBuilder<TestCard> type)
    {
        type.Named("optionalCreateOnlyKeyCard", TestBoardType.ScopeKindName).MaxRecordsPerScope(1);
        type.Field("done", card => card.Done, card => card.DoneChangedAt, SyncFieldTypes.Boolean());
        type.CreateOnly("category", card => card.Text, SyncFieldTypes.Key(["dairy", "bakery"]));
    }
}
