using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class TestBoardType : SyncDataType<TestBoard>
{
    public const string DataTypeName = "testBoard";

    public const string ScopeKindName = "testBoard";

    public const string FailingTitle = "rule-fail";

    protected override void Define(SyncDataTypeBuilder<TestBoard> type)
    {
        type.Named(DataTypeName, ScopeKindName).ScopeRoot(maxOwnedScopesPerAccount: 5).MaxRecordsPerScope(1);
        type.Field("title", board => board.Title, board => board.TitleChangedAt, SyncFieldTypes.Text(0, 100)).RequiredOnCreate();
    }

    protected override SyncRuleDecision Evaluate(SyncRuleContext<TestBoard> context) =>
        context.Record.Title == FailingTitle
            ? throw new InvalidOperationException("The test board rule fails on purpose.")
            : SyncRuleDecision.Continue;
}
