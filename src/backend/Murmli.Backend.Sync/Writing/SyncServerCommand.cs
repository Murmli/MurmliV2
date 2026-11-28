using System.Text.Json;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Sync.Writing;

public sealed record SyncServerCommand(
    Guid CommandId,
    SyncActor Actor,
    SyncCommandKind Kind,
    Guid ScopeId,
    string DataType,
    Guid RecordId,
    IReadOnlyDictionary<string, JsonElement>? Fields = null,
    Func<SyncRecord, bool>? Precondition = null
);
