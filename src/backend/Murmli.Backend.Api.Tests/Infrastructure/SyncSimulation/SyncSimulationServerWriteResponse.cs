namespace Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;

internal sealed record SyncSimulationServerWriteResponse(string Status, string? Reason, string? Cause);
