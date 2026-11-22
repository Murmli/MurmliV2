using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class ZeroRootBoardType : SyncDataType<TestBoard>
{
    protected override void Define(SyncDataTypeBuilder<TestBoard> type)
    {
        type.Named("zeroRootBoard", "zeroRootBoard").ScopeRoot(0).MaxRecordsPerScope(1);
        type.Field("title", board => board.Title, board => board.TitleChangedAt, SyncFieldTypes.Text(0, 100));
    }
}
