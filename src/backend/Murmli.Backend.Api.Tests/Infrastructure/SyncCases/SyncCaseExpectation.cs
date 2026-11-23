namespace Murmli.Backend.Api.Tests.Infrastructure.SyncCases;

internal sealed record SyncCaseExpectation(
    IReadOnlyDictionary<string, SyncCaseResult> Results,
    IReadOnlyList<SyncCaseRecord> Records,
    IReadOnlyList<SyncCaseRecordReference> Absent
);
