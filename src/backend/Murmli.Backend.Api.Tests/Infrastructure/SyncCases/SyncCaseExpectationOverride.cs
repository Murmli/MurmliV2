namespace Murmli.Backend.Api.Tests.Infrastructure.SyncCases;

internal sealed record SyncCaseExpectationOverride(
    IReadOnlyDictionary<string, SyncCaseResult>? Results = null,
    IReadOnlyList<SyncCaseRecord>? Records = null,
    IReadOnlyList<SyncCaseRecordReference>? Absent = null
);
