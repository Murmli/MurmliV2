using Murmli.Backend.ProductImport;
using Murmli.Backend.ProductImport.Extensions;

var builder = Host.CreateApplicationBuilder(args);
builder.AddProductImport();

using var host = builder.Build();
await host.StartAsync();

await using (var scope = host.Services.CreateAsyncScope())
{
    await scope
        .ServiceProvider.GetRequiredService<ProductImporter>()
        .ImportAsync(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

await host.StopAsync();
