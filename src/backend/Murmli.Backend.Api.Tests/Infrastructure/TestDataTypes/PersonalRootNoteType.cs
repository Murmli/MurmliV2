using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class PersonalRootNoteType : SyncDataType<TestNote>
{
    protected override void Define(SyncDataTypeBuilder<TestNote> type)
    {
        type.Named("personalRootNote", SyncScope.PersonalKind).ScopeRoot(1).MaximumRecordsPerScope(1);
        type.Field("title", note => note.Title, note => note.TitleChangedAt, SyncFieldTypes.Text(0, 100));
    }
}
