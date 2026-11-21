using System.Text.Json;

namespace Murmli.Backend.Api.Shopping;

public sealed record SyncRequest(string? Token, IReadOnlyList<SyncCommand>? Commands);

public sealed record SyncCommand(Guid Id, string? Type, DateTimeOffset? OccurredAt, JsonElement Arguments);

internal sealed record SyncCommandArguments(
    Guid? ListId,
    Guid? ItemId,
    string? Name,
    double? Quantity,
    string? Unit,
    string? Category,
    bool? Checked
);
