using System.Text.Json;

namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncRecordEntry(string DataType, Guid Id, JsonElement Fields);
