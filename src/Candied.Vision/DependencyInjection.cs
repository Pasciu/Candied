using Microsoft.Extensions.DependencyInjection;
using Candied.Vision.Adapters;
using Candied.Vision.Ports;

namespace Candied.Vision;

public static class DependencyInjection
{
    public static IServiceCollection AddVisionInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IVisionSensorClient>(_ => new HttpVisionSensorClientAdapter());
        services.AddSingleton<IGameControlProfileStore, JsonGameControlProfileStoreAdapter>();
        services.AddSingleton<IStudioSessionManager>(_ => new DiskStudioSessionManagerAdapter());
        services.AddSingleton<IStudioHostClient>(sp => new HttpStudioHostClientAdapter(sp.GetRequiredService<IStudioSessionManager>()));
        services.AddSingleton<IRecentTargetsStore>(_ => new JsonRecentTargetsStoreAdapter());
        return services;
    }
}
