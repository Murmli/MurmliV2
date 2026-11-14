using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.ProductImport;

public sealed partial class ProductImporter(
    MurmliDbContext context,
    OpenFoodFactsExportReader reader,
    ProductSynchronizer synchronizer,
    IOptions<ProductImportOptions> options,
    TimeProvider timeProvider,
    ILogger<ProductImporter> logger
)
{
    public async Task ImportAsync(CancellationToken cancellationToken = default)
    {
        await context.Database.MigrateAsync(cancellationToken);
        await FailInterruptedRunsAsync(cancellationToken);
        var run = await StartRunAsync(cancellationToken);

        try
        {
            await ImportProductsAsync(run, cancellationToken);
        }
        catch
        {
            await RecordFailureAsync(run.Id);
            throw;
        }
    }

    // A run still marked as running was killed, its real end is unknown
    private Task FailInterruptedRunsAsync(CancellationToken cancellationToken) =>
        context
            .ProductImportRuns.Where(run => run.Status == ProductImportRunStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(run => run.Status, ProductImportRunStatus.Failed),
                cancellationToken
            );

    private async Task<ProductImportRun> StartRunAsync(CancellationToken cancellationToken)
    {
        var run = new ProductImportRun { StartedAt = timeProvider.GetUtcNow(), Status = ProductImportRunStatus.Running };
        context.ProductImportRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task ImportProductsAsync(ProductImportRun run, CancellationToken cancellationToken)
    {
        var productLimit = options.Value.ProductLimit;
        LogProductImportStarted(run.Id, productLimit);

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

        var peakMemoryBytes = ReadPeakMemoryBytes();

        // Saved in the import transaction, so the products and the success only become visible together
        run.Status = ProductImportRunStatus.Succeeded;
        run.FinishedAt = timeProvider.GetUtcNow();
        run.PeakMemoryBytes = peakMemoryBytes;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogProductsSynchronized(result.InsertedCount, result.UpdatedCount, result.DeletedCount, peakMemoryBytes);
    }

    private async Task RecordFailureAsync(long runId)
    {
        try
        {
            // Not cancellable, a cancelled import is recorded as failed as well. The status check keeps a success
            // whose COMMIT reached the server before the connection dropped: the update waits for the row lock
            await context
                .ProductImportRuns.Where(run => run.Id == runId && run.Status == ProductImportRunStatus.Running)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(run => run.Status, ProductImportRunStatus.Failed)
                            .SetProperty(run => run.FinishedAt, timeProvider.GetUtcNow())
                            .SetProperty(run => run.PeakMemoryBytes, ReadPeakMemoryBytes()),
                    CancellationToken.None
                );
        }
        catch (Exception exception)
        {
            LogRunFailureNotRecorded(exception, runId);
        }
    }

    // The job process runs nothing but the import, so the peak of the process is the peak of the run
    private static long ReadPeakMemoryBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PeakWorkingSet64;
    }
}
