namespace Murmli.Backend.Api.Tests.Infrastructure.SyncLoad;

internal sealed record SyncLoadContainerReading(DateTime Read, ulong CpuNanoseconds, ulong MemoryBytes, ulong MemoryLimitBytes);
