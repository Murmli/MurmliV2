using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;

internal sealed class MisfilledNoteType : SyncDataType<TestNote>
{
    protected override void Define(SyncDataTypeBuilder<TestNote> type)
    {
        type.Named("misfilledNote", SyncScope.PersonalKind).MaxRecordsPerScope(1);
        type.Field("title", note => note.Title, note => note.TitleChangedAt, SyncFieldTypes.Text(0, 100));
        type.ServerField("pinned", note => note.Pinned, SyncFieldTypes.Boolean(), SyncServerFill.ActingMemberOnApply);
    }
}
