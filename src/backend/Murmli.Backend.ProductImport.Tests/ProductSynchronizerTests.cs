using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Products;
using Murmli.Backend.ProductImport.Synchronization;

namespace Murmli.Backend.ProductImport.Tests;

public sealed class ProductSynchronizerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly IHost host = ProductImportHost.Create(postgres.CreateDatabaseConnectionString());

    [Fact]
    public async Task SynchronizeAsync_UnchangedProducts_ReturnsNoChanges()
    {
        Product[] products = [CreateProduct("4000000000011", 0.2), CreateProduct("4000000000012", 0.1)];
        await SynchronizeAsync(products, deleteMissing: true);

        Assert.Equal(new ProductSyncResult(0, 0, 0), await SynchronizeAsync(products, deleteMissing: true));
    }

    [Theory]
    [InlineData(true, 1, new[] { "4000000000011", "4000000000013" })]
    [InlineData(false, 0, new[] { "4000000000011", "4000000000012", "4000000000013" })]
    public async Task SynchronizeAsync_NewChangedAndMissingProduct_ReturnsCountsAndDeletesOnlyWhenRequested(
        bool deleteMissing,
        int expectedDeletedCount,
        string[] expectedCodes
    )
    {
        await SynchronizeAsync(
            [CreateProduct("4000000000011", 0.2), CreateProduct("4000000000012", 0.1)],
            deleteMissing: true
        );

        var result = await SynchronizeAsync(
            [CreateProduct("4000000000011", 0.3), CreateProduct("4000000000013", 83)],
            deleteMissing
        );

        Assert.Equal(new ProductSyncResult(1, 1, expectedDeletedCount), result);
        Assert.Equal(expectedCodes, (await ProductImportHost.ReadProductsAsync(host)).Select(product => product.Code));
    }

    public void Dispose() => host.Dispose();

    private static Product CreateProduct(string code, double fat) =>
        new()
        {
            Code = code,
            Name = "Produkt",
            IsPerHundredMilliliters = false,
            Fat = fat,
        };

    private async Task<ProductSyncResult> SynchronizeAsync(IReadOnlyCollection<Product> products, bool deleteMissing)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MurmliDbContext>();
        await context.Database.MigrateAsync(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await scope
            .ServiceProvider.GetRequiredService<ProductSynchronizer>()
            .SynchronizeAsync(products, deleteMissing, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
