namespace Murmli.Backend.ProductImport.Synchronization;

public sealed record ProductSyncResult(int InsertedCount, int UpdatedCount, int DeletedCount);
