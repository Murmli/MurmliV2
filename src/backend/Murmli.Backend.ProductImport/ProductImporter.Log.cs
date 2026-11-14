using Microsoft.Extensions.Logging;

namespace Murmli.Backend.ProductImport;

public sealed partial class ProductImporter
{
    [LoggerMessage(
        EventId = LogEvents.ProductImportStarted,
        EventName = nameof(LogEvents.ProductImportStarted),
        Level = LogLevel.Information,
        Message = "Starting the product import with the product limit {" + LogProperties.ProductLimit + "}"
    )]
    private partial void LogProductImportStarted(int? productLimit);

    [LoggerMessage(
        EventId = LogEvents.GermanProductsRead,
        EventName = nameof(LogEvents.GermanProductsRead),
        Level = LogLevel.Information,
        Message = "Read {"
            + LogProperties.ProductCount
            + "} German products, export read completely: {"
            + LogProperties.IsExportComplete
            + "}"
    )]
    private partial void LogGermanProductsRead(int productCount, bool isExportComplete);

    [LoggerMessage(
        EventId = LogEvents.ProductsSynchronized,
        EventName = nameof(LogEvents.ProductsSynchronized),
        Level = LogLevel.Information,
        Message = "Synchronized products: {"
            + LogProperties.InsertedCount
            + "} inserted, {"
            + LogProperties.UpdatedCount
            + "} updated, {"
            + LogProperties.DeletedCount
            + "} deleted"
    )]
    private partial void LogProductsSynchronized(int insertedCount, int updatedCount, int deletedCount);
}
