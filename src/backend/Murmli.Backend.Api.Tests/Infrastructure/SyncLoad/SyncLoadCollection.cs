namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

// Tests running beside the load tests would compete with the containers for the machine
[CollectionDefinition(DisableParallelization = true)]
public sealed class SyncLoadCollection : ICollectionFixture<SyncLoadEnvironment>;
