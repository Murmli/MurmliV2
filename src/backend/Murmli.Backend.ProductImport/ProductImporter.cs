using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.ProductImport;

public sealed partial class ProductImporter(
    MurmliDbContext context,
    OpenFoodFactsExportReader reader,
    ProductSynchronizer synchronizer,
    IOptions<ProductImportOptions> options,
    ILogger<ProductImporter> logger
)
{
    public async Task ImportAsync(CancellationToken cancellationToken = default)
    {
        await context.Database.MigrateAsync(cancellationToken);

        var productLimit = options.Value.ProductLimit;
        LogProductImportStarted(productLimit);

        var products = new Dictionary<string, Product>();
        var isExportComplete = true;
        await foreach (var product in reader.ReadGermanProductsAsync(cancellationToken))
        {
            products[product.Code] = product;
            if (products.Count >= productLimit)
            {
                isExportComplete = false;
                break;
            }
        }

        LogGermanProductsRead(products.Count, isExportComplete);

        // Buffered in memory on purpose: a COPY during the 30 to 60 minute download would hold the transaction open
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await synchronizer.SynchronizeAsync(
            products.Values,
            deleteMissing: isExportComplete,
            cancellationToken
        );
        await transaction.CommitAsync(cancellationToken);

        LogProductsSynchronized(result.InsertedCount, result.UpdatedCount, result.DeletedCount);
    }
}
