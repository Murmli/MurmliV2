namespace Murmli.Backend.ProductImport;

public sealed record ProductSyncResult(int InsertedCount, int UpdatedCount, int DeletedCount);
