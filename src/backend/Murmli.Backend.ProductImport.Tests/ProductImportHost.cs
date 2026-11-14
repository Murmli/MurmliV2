using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.ProductImport.Tests;

internal static class ProductImportHost
{
    public static IHost Create(string connectionString, byte[]? export = null, int? productLimit = null)
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
        builder
            .Services.AddHttpClient<OpenFoodFactsExportReader>()
            .ConfigurePrimaryHttpMessageHandler(() => new ExportHttpMessageHandler(export ?? []));
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
}
