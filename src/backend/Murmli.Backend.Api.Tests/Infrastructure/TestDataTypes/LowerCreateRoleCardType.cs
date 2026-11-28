using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class LowerCreateRoleCardType : SyncDataType<TestCard>
{
    protected override void Define(SyncDataTypeBuilder<TestCard> type)
    {
        type.Named("lowerCreateRoleCard", TestBoardType.ScopeKindName).MaximumRecordsPerScope(1);
        type.Field("text", card => card.Text, card => card.TextChangedAt, SyncFieldTypes.Text(0, 100));
    }

    public override SyncMemberRole MinimumRole(SyncCommandKind kind) =>
        kind == SyncCommandKind.SetFields ? SyncMemberRole.Owner : SyncMemberRole.Member;
}
