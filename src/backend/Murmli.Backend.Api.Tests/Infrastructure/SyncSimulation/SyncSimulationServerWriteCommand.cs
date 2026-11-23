using System.Text.Json;

namespace Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;

internal sealed record SyncSimulationServerWriteCommand(
    string? Kind,
    Guid? ScopeId,
    string? DataType,
    Guid? RecordId,
    Dictionary<string, JsonElement>? Fields
);
