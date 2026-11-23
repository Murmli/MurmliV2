namespace Murmli.Backend.Api.Tests.Infrastructure.SyncCases;

internal sealed record SyncCase(
    int Format,
    string Id,
    string Rule,
    IReadOnlyList<string> DataTypes,
    IReadOnlyDictionary<string, SyncCaseDevice> Devices,
    IReadOnlyList<SyncCaseCommand> Given,
    IReadOnlyList<SyncCaseCommand> Race,
    SyncCaseExpectation Expect,
    IReadOnlyDictionary<string, SyncCaseExpectationOverride>? ExpectByOrder = null
);
