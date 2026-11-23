namespace Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;

internal sealed record SyncSimulationServerWriteRequest(string? Subject, SyncSimulationServerWriteCommand? Command);
