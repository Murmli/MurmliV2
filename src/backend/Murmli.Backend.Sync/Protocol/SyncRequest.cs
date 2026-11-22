using System.Text.Json;

namespace Murmli.Backend.Sync.Protocol;

public sealed record SyncRequest
{
    public string? Token { get; init; }

    public required IReadOnlyList<string> DataTypes { get; init; }

    public required IReadOnlyList<JsonElement> Commands { get; init; }

    public long? PendingCommandCount { get; init; }

    public long? OldestPendingCommandAgeSeconds { get; init; }
}
