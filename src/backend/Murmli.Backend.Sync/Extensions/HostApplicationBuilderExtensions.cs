using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Murmli.Backend.Sync.Accounts;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Processing;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Sync.Extensions;

public static class HostApplicationBuilderExtensions
{
    public static void AddSync(this IHostApplicationBuilder builder)
    {
        builder
            .Services.AddOptions<SyncOptions>()
            .BindConfiguration(SyncOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SyncOptions>, SyncConfigurationValidator>();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SyncDataTypeRegistry>();
        builder.Services.AddSingleton<SyncCommandParser>();
        builder.Services.AddScoped<SyncCommandUnit>();
        builder.Services.AddScoped<SyncAccountLifecycle>();
    }
}
