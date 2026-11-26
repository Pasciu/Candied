using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.Capabilities;

public interface ITargetPlatform
{
    string PlatformId { get; }
    string DisplayName { get; }
    int Priority { get; }

    bool CanHandle(string targetPath, string? runnerPath = null);

    Task<Result<TargetDescriptor, DetectionError>> InspectAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default);

    IEnvironmentSetupCapability? EnvironmentSetup { get; }
    ICodeKnowledgeCapability? CodeKnowledge { get; }
    IAssetPipelineCapability? AssetPipeline { get; }
    IModWorkspaceCapability? ModWorkspace { get; }
    IRuntimeLogCapability? RuntimeLogs { get; }
}

public interface IEnvironmentSetupCapability
{
    Task<Result<ProvisioningReport, ProvisioningError>> ProvisionAsync(
        TargetDescriptor target,
        ProvisioningSpec spec,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default);

    Task<Result<RollbackReport, ProvisioningError>> RollbackAsync(
        TargetDescriptor target,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default);
}

public interface ICodeKnowledgeCapability
{
    Task<Result<CodeExtractionOutput, CodeExtractionError>> ExtractAndIndexModuleAsync(
        TargetDescriptor target,
        string moduleName,
        CandiedStorageLayout storageLayout,
        ISymbolIndex symbolIndex,
        IProgress<CodeExtractionProgress>? progress = null,
        CancellationToken ct = default);
}

public interface IAssetPipelineCapability
{
    Task<Result<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>> DiscoverAssetsAsync(
        TargetDescriptor target,
        AssetDiscoveryFilter filter,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>> ExtractAssetsAsync(
        TargetDescriptor target,
        AssetExtractionFilter filter,
        string outputDirectory,
        CancellationToken ct = default);
}

public interface IModWorkspaceCapability
{
    bool CanBuildProject(string projectOrManifestPath);

    Task<Result<ModProjectManifest, ScaffoldError>> ScaffoldModAsync(
        ModScaffoldSpec spec,
        CancellationToken ct = default);

    Task<Result<BuildReport, BuildExecutionError>> BuildModAsync(
        string projectOrManifestPath,
        string configuration = "Debug",
        TargetDescriptor? activeTarget = null,
        CancellationToken ct = default);
}

public interface IRuntimeLogCapability
{
    string? ResolveDefaultLogPath(TargetDescriptor target);

    Task<Result<IReadOnlyList<LogEntry>, LogMonitorError>> ReadRecentLogsAsync(
        string logFilePath,
        int maxLines = 100,
        CancellationToken ct = default);
}

public interface IPlatformToolProvider
{
    string PlatformId { get; }

    IReadOnlyList<PlatformToolDefinition> GetTools();

    Task<PlatformToolResult> ExecuteToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string?> arguments,
        TargetDescriptor? activeTarget,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default);
}

public interface IPlatformGuideProvider
{
    string PlatformId { get; }

    PlatformModdingGuide GetGuide();
}
