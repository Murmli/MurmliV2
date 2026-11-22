using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestCardType : SyncDataType<TestCard>
{
    public const string DataTypeName = "testCard";

    protected override void Define(SyncDataTypeBuilder<TestCard> type)
    {
        type.Named(DataTypeName, TestBoardType.ScopeKindName).MaxRecordsPerScope(50);
        type.Field("text", card => card.Text, card => card.TextChangedAt, SyncFieldTypes.Text(1, 100)).RequiredOnCreate();
        type.Group(
            "amount",
            card => card.AmountChangedAt,
            amount =>
            {
                amount.Field("count", card => card.Count, SyncFieldTypes.Decimal(0m, 1000m, maximumScale: 3));
                amount.Field("measure", card => card.Measure, SyncFieldTypes.NullableKey(["piece", "liter"]));
            }
        );
        type.Field("done", card => card.Done, card => card.DoneChangedAt, SyncFieldTypes.Boolean());
        type.ServerField("changedBy", card => card.ChangedByMemberId, SyncFieldTypes.MemberReference(), SyncServerFill.ActingMemberOnApply);
        type.ServerField(
            "createdBy",
            card => card.CreatedByMemberId,
            SyncFieldTypes.MemberReference(),
            SyncServerFill.ActingMemberOnCreate,
            isSentToDevices: false
        );
        type.ServerField("doneBy", card => card.DoneByMemberId, SyncFieldTypes.MemberReference());
        type.ServerField("createdAt", card => card.CreatedAt, SyncFieldTypes.Timestamp(), SyncServerFill.CommandTimeOnCreate);
    }

    public override SyncMemberRole MinimumRole(SyncCommandKind kind) =>
        kind == SyncCommandKind.Delete ? SyncMemberRole.Owner : SyncMemberRole.Member;

    protected override SyncRuleDecision Evaluate(SyncRuleContext<TestCard> context)
    {
        context.Record.DoneByMemberId = context.Record.Done ? context.Record.DoneByMemberId ?? context.ActingMemberId : null;
        return SyncRuleDecision.Continue;
    }
}
