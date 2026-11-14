using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.ProductImport.Configuration;
using Murmli.Backend.ProductImport.OpenFoodFacts;
using Murmli.Backend.ProductImport.Synchronization;

namespace Murmli.Backend.ProductImport.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static void AddProductImport(this IHostApplicationBuilder builder)
    {
        // The retrying execution strategy conflicts with the user-initiated import transaction
        builder.AddMurmliDbContext(settings => settings.DisableRetry = true);

        builder
            .Services.AddOptions<ProductImportOptions>()
            .BindConfiguration(ProductImportOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // A retry could not resume the stream, a failed download fails the run
        builder.Services.AddHttpClient<OpenFoodFactsExportReader>(client =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Murmli/1.0 (+https://github.com/Murmli/MurmliV2)")
        );

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ProductSynchronizer>();
        builder.Services.AddScoped<ProductImporter>();
    }
}
