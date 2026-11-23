using Microsoft.Extensions.DependencyInjection;
using Candied.Core.Ports;
using Candied.Workspace.Adapters;

namespace Candied.Workspace;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkspaceInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(CandiedWorkspaceStorage.GetLayout());
        services.AddSingleton<IBuildRunner, DotNetBuildRunnerAdapter>();
        services.AddSingleton<IProjectGenerator, DiskProjectGeneratorAdapter>();
        services.AddSingleton<IProjectReferenceEditor, DiskProjectReferenceEditorAdapter>();
        services.AddSingleton<ILogMonitor, BepInExLogMonitorAdapter>();
        services.AddSingleton<IDocumentationGenerator, DiskDocumentationGeneratorAdapter>();
        services.AddSingleton<IEnvironmentProvisioner, BepInExEnvironmentProvisionerAdapter>();
        services.AddSingleton<IGameProcessManager, OsGameProcessManagerAdapter>();
        services.AddSingleton<IVisionBridgeClient, HttpVisionBridgeClientAdapter>();
        services.AddSingleton<IStudioSessionManager, DiskStudioSessionManagerAdapter>();
        services.AddSingleton<IRegisteredGameRegistry, JsonFileGameRegistryAdapter>();
        services.AddSingleton<IStudioClient, HttpStudioClientAdapter>();
        return services;
    }
}
