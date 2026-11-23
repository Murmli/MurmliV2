using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Sync.Accounts;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.Configuration;
using Murmli.Backend.Sync.DataTypes;
using Murmli.Backend.Sync.Diagnostics;
using Murmli.Backend.Sync.Processing;
using Murmli.Backend.Sync.Protocol;
using Murmli.Backend.Sync.Writing;

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
        builder.Services.AddSingleton<SyncTelemetry>();
        builder.Services.AddScoped<SyncCommandUnit>();
        builder.Services.AddScoped<SyncAccountLifecycle>();
        builder.Services.AddScoped<SyncChangeReader>();
        builder.Services.AddSingleton(provider => new SyncCleanupRunner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<SyncOptions>>(),
            provider.GetRequiredService<SyncTelemetry>(),
            provider.GetRequiredService<ILogger<SyncCleanupRunner>>()
        ));
        builder.Services.AddScoped(provider => new SyncProcessor(
            provider.GetRequiredService<SyncDataTypeRegistry>(),
            provider.GetRequiredService<SyncCommandParser>(),
            provider.GetRequiredService<SyncCommandUnit>(),
            provider.GetRequiredService<SyncAccountLifecycle>(),
            provider.GetRequiredService<SyncChangeReader>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<SyncOptions>>(),
            provider.GetRequiredService<SyncTelemetry>(),
            provider.GetRequiredService<ILogger<SyncProcessor>>()
        ));
        builder.Services.AddScoped(provider => new SyncServerWriter(
            provider.GetRequiredService<SyncDataTypeRegistry>(),
            provider.GetRequiredService<SyncCommandUnit>(),
            provider.GetRequiredService<MurmliDbContext>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<SyncTelemetry>(),
            provider.GetRequiredService<ILogger<SyncServerWriter>>()
        ));
    }
}
