using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Capabilities;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface ITargetPlatformRegistry
{
    IReadOnlyList<ITargetPlatform> GetAllPlatforms();

    ITargetPlatform? GetPlatformById(string platformId);

    ITargetPlatform ResolvePlatform(string targetPath, string? runnerPath = null);

    ITargetPlatform? ResolveModBuildPlatform(string projectOrManifestPath);

    Task<Result<TargetDescriptor, DetectionError>> InspectTargetAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default);
}

public interface ISymbolIndex
{
    Task<Result<Unit, IndexError>> IndexCSharpSourceTreeAsync(
        string sourceDirectory,
        string? moduleName = null,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<Unit, IndexError>> IndexExtractedSymbolsAsync(
        string moduleName,
        IReadOnlyList<SymbolSummary> symbols,
        IReadOnlyList<TypeDefinitionDetail> typeDetails,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> SearchSymbolsAsync(
        SymbolQuery query,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<CodeSearchMatch>, IndexError>> SearchCodeAsync(
        CodeSearchQuery query,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<SymbolSummary>, IndexError>> GetAllSymbolsAsync(
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<TypeDefinitionDetail, IndexError>> GetTypeDetailAsync(
        string fullTypeName,
        string? databasePath = null,
        CancellationToken ct = default);
}

public interface IAssetIndex
{
    Task<Result<int, IndexError>> IndexAssetsAsync(
        TargetDescriptor target,
        IReadOnlyList<DiscoveredAssetInfo> assets,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<DiscoveredAssetInfo>, IndexError>> SearchAssetsAsync(
        AssetDiscoveryFilter filter,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<AssetDiscoveryPage, IndexError>> SearchAssetsPageAsync(
        AssetDiscoveryFilter filter,
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<bool, IndexError>> HasIndexedAssetsAsync(
        string? databasePath = null,
        CancellationToken ct = default);

    Task<Result<int, IndexError>> ClearIndexAsync(
        string? databasePath = null,
        CancellationToken ct = default);
}

public interface IDocumentationGenerator
{
    Task<Result<Unit, ScaffoldError>> GenerateKnowledgeHubAsync(
        TargetArchitectureOverview overview,
        string docsDirectoryPath,
        CancellationToken ct = default);

    Task<Result<Unit, ScaffoldError>> GenerateKnowledgeHubAsync(
        TargetDescriptor target,
        IReadOnlyList<SymbolSummary> symbols,
        string docsDirectoryPath,
        CancellationToken ct = default);

    Task<Result<Unit, ScaffoldError>> SaveResearchNotesAsync(
        string targetName,
        string markdownContent,
        string docsDirectoryPath,
        CancellationToken ct = default);
}

public interface ITargetProcessManager
{
    Task<Result<TargetProcessInfo?, ProcessManagementError>> GetRunningTargetAsync(
        TargetDescriptor target,
        CancellationToken ct = default);

    Task<Result<TargetProcessInfo, ProcessManagementError>> LaunchTargetAsync(
        TargetDescriptor target,
        TargetLaunchOptions options,
        CancellationToken ct = default);

    Task<Result<Unit, ProcessManagementError>> StopTargetAsync(
        TargetDescriptor target,
        CancellationToken ct = default);
}
