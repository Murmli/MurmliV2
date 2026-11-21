namespace Murmli.Backend.Api.Shopping;

public sealed record SyncResponse(
    IReadOnlyList<SyncCommandResult> Results,
    string Token,
    IReadOnlyList<SyncListResponse> Lists,
    IReadOnlyList<SyncItemResponse> Items,
    IReadOnlyList<Guid> RemovedListIds
);

public sealed record SyncCommandResult(Guid Id, string Status, string? Reason)
{
    public static SyncCommandResult Applied(Guid id) => new(id, "applied", null);

    public static SyncCommandResult Ignored(Guid id, string reason) => new(id, "ignored", reason);

    public static SyncCommandResult Rejected(Guid id, string reason) => new(id, "rejected", reason);

    public static SyncCommandResult Duplicate(Guid id) => new(id, "duplicate", null);
}

public sealed record SyncListResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Snapshot,
    IReadOnlyList<SyncListMemberResponse> Members
);

public sealed record SyncListMemberResponse(Guid Id, Guid UserId, DateTimeOffset CreatedAt);

public sealed record SyncItemResponse(
    Guid Id,
    Guid? ListId,
    string Name,
    double? Quantity,
    string? Unit,
    string? Category,
    bool Checked,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Deleted
);
