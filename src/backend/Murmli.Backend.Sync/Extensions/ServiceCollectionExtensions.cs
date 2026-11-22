using Microsoft.Extensions.DependencyInjection;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Sync.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSyncDataType<TType>(this IServiceCollection services)
        where TType : SyncDataType => services.AddSingleton<SyncDataType, TType>();
}
