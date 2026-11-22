using System.Collections.Frozen;
using System.Text.Json;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Extensions;

namespace Murmli.Backend.Sync.Protocol;

internal sealed class SyncCommandParser(SyncDataTypeRegistry registry)
{
    private const string IdMember = "id";

    private const string KindMember = "kind";

    private const string ScopeIdMember = "scopeId";

    private const string DataTypeMember = "dataType";

    private const string RecordIdMember = "recordId";

    private const string TimeMember = "time";

    private const string FieldsMember = "fields";

    private const long MinimumTime = -62_135_596_800_000;

    private const long MaximumTime = 253_402_300_799_999;

    private static readonly FrozenSet<string> CommandMembers = FrozenSet.Create(
        StringComparer.Ordinal,
        IdMember,
        KindMember,
        ScopeIdMember,
        DataTypeMember,
        RecordIdMember,
        TimeMember,
        FieldsMember
    );

    public SyncParsedCommand Parse(JsonElement command, IReadOnlyList<SyncDataType> listedTypes)
    {
        if (command.ValueKind != JsonValueKind.Object)
        {
            return new SyncParsedCommand(null, null, null, null);
        }

        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var hasUnknownMember = false;
        foreach (var member in command.EnumerateObject())
        {
            if (!member.TryGetReadableName(out var name) || !CommandMembers.Contains(name))
            {
                hasUnknownMember = true;
            }
            else if (!members.TryAdd(name, member.Value))
            {
                // A duplicated member is ambiguous: the default element is unreadable wherever it is used
                members[name] = default;
            }
        }

        var commandId = ReadId(members, IdMember) is { Version: 4 } id ? id : (Guid?)null;
        var scopeId = ReadId(members, ScopeIdMember);
        var recordId = ReadId(members, RecordIdMember);
        var dataTypeName = ReadString(members, DataTypeMember);
        var target =
            scopeId is { } targetScopeId && recordId is { } targetRecordId && dataTypeName is not null && registry.TryGet(dataTypeName, out _)
                ? new SyncCommandTarget(targetScopeId, dataTypeName, targetRecordId)
                : null;
        if (
            commandId is not { } claimedCommandId
            || ReadString(members, KindMember) is not { } kind
            || scopeId is not { Version: 4 } claimedScopeId
            || dataTypeName is null
            || recordId is not { Version: 4 } claimedRecordId
            || ReadTime(members) is not { } time
        )
        {
            return new SyncParsedCommand(commandId, target, null, null);
        }

        var claim = new SyncCommandClaim(
            claimedCommandId,
            kind,
            claimedScopeId,
            dataTypeName,
            claimedRecordId,
            DateTimeOffset.FromUnixTimeMilliseconds(time)
        );
        var claimedTarget = new SyncCommandTarget(claimedScopeId, dataTypeName, claimedRecordId);
        if (
            hasUnknownMember
            || !SyncCommandKind.TryParse(kind, out var commandKind)
            || !registry.TryGet(dataTypeName, out var dataType)
            || !listedTypes.Contains(dataType)
            || claimedRecordId == claimedScopeId
            || !TryParseFields(members, commandKind, dataType, out var changes)
        )
        {
            return new SyncParsedCommand(commandId, claimedTarget, claim, null);
        }

        return new SyncParsedCommand(commandId, claimedTarget, claim, new SyncCommand(claim, commandKind, dataType, changes));
    }

    private static Guid? ReadId(Dictionary<string, JsonElement> members, string name) =>
        ReadString(members, name) is { } text && Guid.TryParseExact(text, "D", out var id) ? id : null;

    private static string? ReadString(Dictionary<string, JsonElement> members, string name) =>
        members.TryGetValue(name, out var element) && element.TryGetReadableString(out var text) ? text : null;

    private static long? ReadTime(Dictionary<string, JsonElement> members) =>
        members.TryGetValue(TimeMember, out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetInt64(out var time)
        && time is >= MinimumTime and <= MaximumTime
            ? time
            : null;

    private static bool TryParseFields(
        Dictionary<string, JsonElement> members,
        SyncCommandKind kind,
        SyncDataType dataType,
        out SyncFieldChanges? changes
    )
    {
        changes = null;
        var hasFields = members.TryGetValue(FieldsMember, out var fields);
        if (kind == SyncCommandKind.Delete)
        {
            return !hasFields || fields.ValueKind == JsonValueKind.Null;
        }

        changes = hasFields ? dataType.ParseFields(fields, kind) : null;
        return changes is not null;
    }
}
