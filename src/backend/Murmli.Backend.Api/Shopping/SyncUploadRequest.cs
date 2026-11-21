using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmli.Backend.Api.Shopping;

public sealed record SyncUploadRequest(IReadOnlyList<SyncOperation>? Operations);

public sealed record SyncOperation(
    [property: JsonPropertyName("op_id")] long OperationId,
    [property: JsonPropertyName("op")] string? Operation,
    string? Type,
    string? Id,
    [property: JsonPropertyName("tx_id")] long? TransactionId,
    IReadOnlyDictionary<string, JsonElement>? Data
);
