using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestNoteType : SyncDataType<TestNote>
{
    public const string DataTypeName = "testNote";

    public const string RejectingTitle = "rule-reject";

    public const string IgnoringTitle = "rule-ignore";

    public const string FailingTitle = "rule-fail";

    protected override void Define(SyncDataTypeBuilder<TestNote> type)
    {
        type.Named(DataTypeName, SyncScope.PersonalKind).MaxRecordsPerScope(50);
        type.Field("title", note => note.Title, note => note.TitleChangedAt, SyncFieldTypes.Text(0, 100)).RequiredOnCreate();
        type.Group(
            "style",
            note => note.StyleChangedAt,
            style =>
            {
                style.Field("color", note => note.Color, SyncFieldTypes.NullableKey(["red", "green", "blue"]));
                style.Field("weight", note => note.Weight, SyncFieldTypes.Decimal(0m, 100m, maximumScale: 1));
            }
        );
        type.ServerField("pinned", note => note.Pinned, SyncFieldTypes.Boolean());
    }

    protected override SyncRuleDecision Evaluate(SyncRuleContext<TestNote> context) =>
        context.Record.Title switch
        {
            RejectingTitle => SyncRuleDecision.Reject(SyncRejectionReason.Invalid),
            IgnoringTitle => SyncRuleDecision.Ignore(SyncIgnoreCause.LostToNewer),
            FailingTitle => throw new InvalidOperationException("The test note rule fails on purpose."),
            _ => SyncRuleDecision.Continue,
        };
}
