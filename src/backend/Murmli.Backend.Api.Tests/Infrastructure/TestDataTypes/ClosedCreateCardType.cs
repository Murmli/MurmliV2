using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class ClosedCreateCardType : SyncDataType<TestCard>
{
    protected override void Define(SyncDataTypeBuilder<TestCard> type)
    {
        type.Named("closedCreateCard", TestBoardType.ScopeKindName).MaximumRecordsPerScope(1);
        type.Field("text", card => card.Text, card => card.TextChangedAt, SyncFieldTypes.Text(0, 100)).RequiredOnCreate();
    }

    public override SyncMemberRole MinimumRole(SyncCommandKind kind) =>
        kind == SyncCommandKind.Create ? new SyncMemberRole("nobody") : SyncMemberRole.Member;
}
