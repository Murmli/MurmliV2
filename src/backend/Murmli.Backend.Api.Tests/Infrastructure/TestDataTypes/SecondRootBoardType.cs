using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class SecondRootBoardType : SyncDataType<TestBoard>
{
    protected override void Define(SyncDataTypeBuilder<TestBoard> type)
    {
        type.Named("secondBoard", TestBoardType.ScopeKindName).ScopeRoot(1).MaximumRecordsPerScope(1);
        type.Field("title", board => board.Title, board => board.TitleChangedAt, SyncFieldTypes.Text(0, 100));
    }
}
