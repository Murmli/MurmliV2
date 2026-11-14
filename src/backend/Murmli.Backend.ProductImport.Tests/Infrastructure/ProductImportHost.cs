using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Products;
using Murmli.Backend.ProductImport.Extensions;
using Murmli.Backend.ProductImport.OpenFoodFacts;

namespace Murmli.Backend.ProductImport.Tests.Infrastructure;

internal static class ProductImportHost
{
    public static IHost Create(
        string connectionString,
        byte[]? export = null,
        int? productLimit = null,
        TimeProvider? timeProvider = null,
        Func<CancellationToken, Task>? onExportRequested = null,
        Func<Stream>? openExport = null
    )
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Production }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:murmli"] = connectionString,
                ["ProductImport:ProductLimit"] = productLimit?.ToString(CultureInfo.InvariantCulture),
            }
        );
        builder.AddProductImport();
        if (timeProvider is not null)
        {
            builder.Services.AddSingleton(timeProvider);
        }

        builder
            .Services.AddHttpClient<OpenFoodFactsExportReader>()
            .ConfigurePrimaryHttpMessageHandler(() =>
                new ExportHttpMessageHandler(openExport ?? (() => new MemoryStream(export ?? [])), onExportRequested)
            );
        return builder.Build();
    }

    public static async Task<Product[]> ReadProductsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<MurmliDbContext>()
            .Products.AsNoTracking()
            .OrderBy(product => product.Code)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<ProductImportRun[]> ReadRunsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<MurmliDbContext>()
            .ProductImportRuns.AsNoTracking()
            .OrderBy(run => run.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }
}
