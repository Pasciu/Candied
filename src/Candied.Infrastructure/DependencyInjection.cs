using Microsoft.Extensions.DependencyInjection;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Infrastructure.Adapters;

namespace Candied.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCandiedCoreAndInfrastructure(
        this IServiceCollection services,
        string? workspaceRoot = null)
    {
        services.AddSingleton(_ => CandiedWorkspaceStorage.GetLayout(workspaceRoot));
        services.AddSingleton<ITargetPlatformRegistry, DefaultTargetPlatformRegistryAdapter>();
        services.AddSingleton<ISymbolIndex, SqliteSymbolIndexAdapter>();
        services.AddSingleton<IAssetIndex, SqliteAssetIndexAdapter>();
        services.AddSingleton<IDocumentationGenerator, DiskDocumentationGeneratorAdapter>();
        services.AddSingleton<ITargetProcessManager, OsTargetProcessManagerAdapter>();

        services.AddTransient<InspectTargetUseCase>();
        services.AddTransient<SetupTargetEnvironmentUseCase>();
        services.AddTransient<RollbackTargetEnvironmentUseCase>();
        services.AddTransient<ExtractCodeModuleUseCase>();
        services.AddTransient<SearchSymbolsUseCase>();
        services.AddTransient<SearchCodeUseCase>();
        services.AddTransient<GetTypeDetailUseCase>();
        services.AddTransient<SaveResearchNotesUseCase>();
        services.AddTransient<IndexAssetsUseCase>();
        services.AddTransient<DiscoverAssetsUseCase>();
        services.AddTransient<ExtractAssetsUseCase>();
        services.AddTransient<ScaffoldModUseCase>();
        services.AddTransient<BuildModUseCase>();
        services.AddTransient<ReadRuntimeLogsUseCase>();
        services.AddTransient<ManageTargetProcessUseCase>();

        return services;
    }
}
