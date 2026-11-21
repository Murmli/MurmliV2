namespace Murmli.Backend.Api.Shopping;

public sealed record SyncUploadResponse(IReadOnlyList<SyncOperationResult> Results);

public sealed record SyncOperationResult(string Id, string Type, string Status, string? Reason)
{
    public static SyncOperationResult Applied(SyncOperation operation) => new(operation.Id ?? "", operation.Type ?? "", "applied", null);

    public static SyncOperationResult Rejected(SyncOperation operation, string reason) =>
        new(operation.Id ?? "", operation.Type ?? "", "rejected", reason);

    public static SyncOperationResult Ignored(SyncOperation operation, string reason) =>
        new(operation.Id ?? "", operation.Type ?? "", "ignored", reason);
}
