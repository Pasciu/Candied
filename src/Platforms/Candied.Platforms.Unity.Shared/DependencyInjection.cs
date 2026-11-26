using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Candied.Platforms.Unity.Shared.Contracts;
using Candied.Platforms.Unity.Shared.Internal;

namespace Candied.Platforms.Unity.Shared;

public static class DependencyInjection
{
    public static IServiceCollection AddUnitySharedServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IUnitySharedInspector, UnitySharedInspector>();
        services.TryAddSingleton<IUnityAssetPipeline, UnityAssetsPipelineCapability>();
        services.TryAddSingleton<IUnityRuntimeLogReader, BepInExRuntimeLogCapability>();
        services.TryAddSingleton<IUnityInputExtractor, UnityInputProfileExtractor>();
        return services;
    }
}
