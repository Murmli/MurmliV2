namespace Murmli.Backend.Api.Tests.Infrastructure;

// Meter and span listeners are process-wide: a host would also record the measurements of hosts running in parallel
[CollectionDefinition(DisableParallelization = true)]
public sealed class TelemetryCollection : ICollectionFixture<PostgresFixture>;
