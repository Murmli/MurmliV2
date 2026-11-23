using Microsoft.Extensions.DependencyInjection;
using Murmli.Backend.Sync.Accounts;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Sync.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSyncDataType<TType>(this IServiceCollection services)
        where TType : SyncDataType => services.AddSingleton<SyncDataType, TType>();

    public static IServiceCollection AddSyncCleanupContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, ISyncCleanupContributor => services.AddScoped<ISyncCleanupContributor, TContributor>();

    public static IServiceCollection AddSyncAccountBootstrapContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, ISyncAccountBootstrapContributor => services.AddScoped<ISyncAccountBootstrapContributor, TContributor>();
}
