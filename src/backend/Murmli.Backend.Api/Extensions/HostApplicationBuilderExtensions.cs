using Microsoft.Extensions.Hosting;
using Murmli.Backend.Data.Extensions;

namespace Murmli.Backend.Api.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static void AddApi(this IHostApplicationBuilder builder)
    {
        builder.AddMurmliDbContext();
    }
}
