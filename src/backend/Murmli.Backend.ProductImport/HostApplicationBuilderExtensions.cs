using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Murmli.Backend.Data;

namespace Murmli.Backend.ProductImport;

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

        builder.Services.AddScoped<ProductSynchronizer>();
        builder.Services.AddScoped<ProductImporter>();
    }
}
