using Microsoft.Extensions.Hosting;

namespace Murmli.Backend.Api.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static void AddApi(this IHostApplicationBuilder builder)
    {
        builder.AddAzureNpgsqlDataSource("murmli");
    }
}
