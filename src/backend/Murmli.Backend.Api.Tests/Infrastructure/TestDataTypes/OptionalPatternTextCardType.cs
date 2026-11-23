using System.Text.RegularExpressions;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed partial class OptionalPatternTextCardType : SyncDataType<TestCard>
{
    protected override void Define(SyncDataTypeBuilder<TestCard> type)
    {
        type.Named("optionalPatternTextCard", TestBoardType.ScopeKindName).MaxRecordsPerScope(1);
        type.Field("text", card => card.Text, card => card.TextChangedAt, SyncFieldTypes.Text(0, 100, LowercaseLetters()));
    }

    [GeneratedRegex(@"\A[a-z]+\z")]
    private static partial Regex LowercaseLetters();
}
