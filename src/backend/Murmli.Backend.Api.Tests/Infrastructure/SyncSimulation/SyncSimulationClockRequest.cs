namespace Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;

internal sealed record SyncSimulationClockRequest(long? AdvanceMilliseconds, long? SetUnixMilliseconds);
