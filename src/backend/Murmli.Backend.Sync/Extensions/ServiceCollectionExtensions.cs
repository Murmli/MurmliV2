using Microsoft.Extensions.DependencyInjection;
using Murmli.Backend.Sync.Cleanup;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Sync.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSyncDataType<TType>(this IServiceCollection services)
        where TType : SyncDataType => services.AddSingleton<SyncDataType, TType>();

    public static IServiceCollection AddSyncCleanupContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, ISyncCleanupContributor => services.AddScoped<ISyncCleanupContributor, TContributor>();
}
