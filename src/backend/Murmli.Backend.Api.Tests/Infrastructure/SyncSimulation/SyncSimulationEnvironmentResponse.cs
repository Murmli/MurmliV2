namespace Murmli.Backend.Api.Tests.Infrastructure.SyncSimulation;

internal sealed record SyncSimulationEnvironmentResponse(IReadOnlyList<string> ApiUrls, long ServerTime, Guid DatabaseIdentity);
