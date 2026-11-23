namespace Murmli.Backend.Sync.DataTypes;

internal sealed record SyncRuleInput(
    Guid? ActingMemberId,
    DateTimeOffset ServerTime,
    IReadOnlySet<string> AppliedProperties,
    Func<string, object?>? ReadOriginal
);
