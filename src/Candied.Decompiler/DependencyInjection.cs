using Candied.Core.Ports;
using Candied.Decompiler.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Candied.Decompiler;

public static class DependencyInjection
{
    public static IServiceCollection AddDecompilerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IGameDetector, UnityGameDetectorAdapter>();
        services.AddSingleton<IDecompilerEngine, ILSpyDecompilerEngineAdapter>();
        services.AddSingleton<ISymbolIndex, SqliteSymbolIndexAdapter>();
        services.AddSingleton<IAssetExtractor, AssetsToolsAssetExtractorAdapter>();
        services.AddSingleton<IAssetIndex, SqliteAssetIndexAdapter>();
        return services;
    }
}
