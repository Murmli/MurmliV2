using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Murmli.Backend.Telemetry.Tests.Infrastructure;

internal static class LocalWebApplication
{
    public static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    public static async Task<Uri> StartAsync(WebApplication application)
    {
        await application.StartAsync(TestContext.Current.CancellationToken);
        return new Uri(application.Urls.Single());
    }
}
