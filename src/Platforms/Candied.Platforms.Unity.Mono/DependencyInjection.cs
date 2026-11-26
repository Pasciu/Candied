using Microsoft.Extensions.DependencyInjection;
using Candied.Core.Capabilities;
using Candied.Platforms.Unity.Mono.Capabilities;
using Candied.Platforms.Unity.Mono.Tools;
using Candied.Platforms.Unity.Shared;

namespace Candied.Platforms.Unity.Mono;

public static class DependencyInjection
{
    public static IServiceCollection AddUnityMonoPlatform(this IServiceCollection services)
    {
        services.AddUnitySharedServices();

        services.AddSingleton<BepInEx5EnvironmentSetupCapability>();
        services.AddSingleton<ILSpyCodeKnowledgeCapability>();
        services.AddSingleton<BepInEx5ModWorkspaceCapability>();

        services.AddSingleton<ITargetPlatform, UnityMonoTargetPlatform>();
        services.AddSingleton<IPlatformToolProvider, UnityMonoPlatformToolProvider>();
        services.AddSingleton<IPlatformGuideProvider, UnityMonoPlatformGuideProvider>();

        return services;
    }
}
