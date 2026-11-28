using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class NullableTitleNoteType : SyncDataType<TestNote>
{
    protected override void Define(SyncDataTypeBuilder<TestNote> type)
    {
        type.Named("brokenNote", SyncScope.PersonalKind).MaximumRecordsPerScope(1);
        type.Field("title", note => note.Title, note => note.TitleChangedAt, SyncFieldTypes.NullableKey(["a"]));
    }
}
