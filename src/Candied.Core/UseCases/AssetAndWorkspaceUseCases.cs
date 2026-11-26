using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class IndexAssetsUseCase(
    ITargetPlatformRegistry platformRegistry,
    IAssetIndex assetIndex)
{
    public async Task<Result<int, DomainError>> ExecuteAsync(
        string targetPath,
        string? runnerPath,
        CandiedStorageLayout storageLayout,
        bool forceReindex = false,
        CancellationToken ct = default)
    {
        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<int, DomainError>(inspectRes.Error);
        }

        var target = inspectRes.Value;
        var platform = platformRegistry.GetPlatformById(target.PlatformId) ?? platformRegistry.ResolvePlatform(targetPath, runnerPath);
        if (platform.AssetPipeline == null)
        {
            return Result.Fail<int, DomainError>(
                new UnsupportedCapabilityError(platform.PlatformId, nameof(PlatformCapability.AssetPipeline)));
        }

        string dbPath = storageLayout.GetDatabasePath(target.Name);
        if (forceReindex)
        {
            await assetIndex.ClearIndexAsync(dbPath, ct).ConfigureAwait(false);
        }

        var scanFilter = new AssetDiscoveryFilter(MaxResults: int.MaxValue);
        var discoveredRes = await platform.AssetPipeline.DiscoverAssetsAsync(target, scanFilter, ct).ConfigureAwait(false);
        if (discoveredRes.IsFailure)
        {
            return Result.Fail<int, DomainError>(discoveredRes.Error);
        }

        var indexRes = await assetIndex.IndexAssetsAsync(target, discoveredRes.Value, dbPath, ct).ConfigureAwait(false);
        return indexRes.IsSuccess
            ? Result.Ok<int, DomainError>(indexRes.Value)
            : Result.Fail<int, DomainError>(indexRes.Error);
    }
}

public sealed class DiscoverAssetsUseCase(
    ITargetPlatformRegistry platformRegistry,
    IAssetIndex? assetIndex = null)
{
    public async Task<Result<AssetDiscoveryPage, DomainError>> ExecutePageAsync(
        string targetPath,
        string? runnerPath,
        AssetDiscoveryFilter filter,
        CandiedStorageLayout storageLayout,
        CancellationToken ct = default)
    {
        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<AssetDiscoveryPage, DomainError>(inspectRes.Error);
        }

        var target = inspectRes.Value;
        string dbPath = storageLayout.GetDatabasePath(target.Name);

        if (assetIndex != null)
        {
            var hasIndexed = await assetIndex.HasIndexedAssetsAsync(dbPath, ct).ConfigureAwait(false);
            if (hasIndexed.IsSuccess && hasIndexed.Value)
            {
                var pageRes = await assetIndex.SearchAssetsPageAsync(filter, dbPath, ct).ConfigureAwait(false);
                if (pageRes.IsSuccess)
                {
                    return Result.Ok<AssetDiscoveryPage, DomainError>(pageRes.Value);
                }
            }
        }

        var platform = platformRegistry.GetPlatformById(target.PlatformId) ?? platformRegistry.ResolvePlatform(targetPath, runnerPath);
        if (platform.AssetPipeline == null)
        {
            return Result.Fail<AssetDiscoveryPage, DomainError>(
                new UnsupportedCapabilityError(platform.PlatformId, nameof(PlatformCapability.AssetPipeline)));
        }

        var rawRes = await platform.AssetPipeline.DiscoverAssetsAsync(target, filter, ct).ConfigureAwait(false);
        if (rawRes.IsFailure)
        {
            return Result.Fail<AssetDiscoveryPage, DomainError>(rawRes.Error);
        }

        return Result.Ok<AssetDiscoveryPage, DomainError>(new AssetDiscoveryPage(rawRes.Value.Count, rawRes.Value));
    }
}

public sealed class ExtractAssetsUseCase(ITargetPlatformRegistry platformRegistry)
{
    public async Task<Result<IReadOnlyList<ExtractedAssetInfo>, DomainError>> ExecuteAsync(
        string targetPath,
        string? runnerPath,
        AssetExtractionFilter filter,
        CandiedStorageLayout storageLayout,
        string? customOutputDir = null,
        CancellationToken ct = default)
    {
        var inspectRes = await platformRegistry.InspectTargetAsync(targetPath, runnerPath, storageLayout, ct).ConfigureAwait(false);
        if (inspectRes.IsFailure)
        {
            return Result.Fail<IReadOnlyList<ExtractedAssetInfo>, DomainError>(inspectRes.Error);
        }

        var target = inspectRes.Value;
        var platform = platformRegistry.GetPlatformById(target.PlatformId) ?? platformRegistry.ResolvePlatform(targetPath, runnerPath);
        if (platform.AssetPipeline == null)
        {
            return Result.Fail<IReadOnlyList<ExtractedAssetInfo>, DomainError>(
                new UnsupportedCapabilityError(platform.PlatformId, nameof(PlatformCapability.AssetPipeline)));
        }

        string outputDir = string.IsNullOrWhiteSpace(customOutputDir)
            ? storageLayout.GetAssetsDir(target.Name)
            : customOutputDir;

        var extRes = await platform.AssetPipeline.ExtractAssetsAsync(target, filter, outputDir, ct).ConfigureAwait(false);
        return extRes.IsSuccess
            ? Result.Ok<IReadOnlyList<ExtractedAssetInfo>, DomainError>(extRes.Value)
            : Result.Fail<IReadOnlyList<ExtractedAssetInfo>, DomainError>(extRes.Error);
    }
}

public sealed class ScaffoldModUseCase(ITargetPlatformRegistry platformRegistry)
{
    public async Task<Result<ModProjectManifest, DomainError>> ExecuteAsync(
        ModScaffoldSpec spec,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.ModName))
        {
            return Result.Fail<ModProjectManifest, DomainError>(new ScaffoldError("Mod name cannot be empty."));
        }

        var platform = platformRegistry.GetPlatformById(spec.Target.PlatformId);
        if (platform?.ModWorkspace == null)
        {
            return Result.Fail<ModProjectManifest, DomainError>(
                new UnsupportedCapabilityError(spec.Target.PlatformId, nameof(PlatformCapability.ModWorkspace)));
        }

        var res = await platform.ModWorkspace.ScaffoldModAsync(spec, ct).ConfigureAwait(false);
        return res.IsSuccess
            ? Result.Ok<ModProjectManifest, DomainError>(res.Value)
            : Result.Fail<ModProjectManifest, DomainError>(res.Error);
    }
}

public sealed class BuildModUseCase(ITargetPlatformRegistry platformRegistry)
{
    public async Task<Result<BuildReport, DomainError>> ExecuteAsync(
        string projectOrManifestPath,
        string configuration = "Debug",
        TargetDescriptor? activeTarget = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectOrManifestPath))
        {
            return Result.Fail<BuildReport, DomainError>(new BuildExecutionError("Project or manifest path cannot be empty."));
        }

        var platform = (activeTarget != null ? platformRegistry.GetPlatformById(activeTarget.PlatformId) : null)
            ?? platformRegistry.ResolveModBuildPlatform(projectOrManifestPath);

        if (platform?.ModWorkspace == null)
        {
            return Result.Fail<BuildReport, DomainError>(
                new BuildExecutionError($"No registered target platform can build '{projectOrManifestPath}'."));
        }

        var buildRes = await platform.ModWorkspace.BuildModAsync(projectOrManifestPath, configuration, activeTarget, ct).ConfigureAwait(false);
        return buildRes.IsSuccess
            ? Result.Ok<BuildReport, DomainError>(buildRes.Value)
            : Result.Fail<BuildReport, DomainError>(buildRes.Error);
    }
}

public sealed class ReadRuntimeLogsUseCase(ITargetPlatformRegistry platformRegistry)
{
    public async Task<Result<IReadOnlyList<LogEntry>, DomainError>> ExecuteAsync(
        string logFilePath,
        int maxLines = 100,
        TargetDescriptor? activeTarget = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            return Result.Fail<IReadOnlyList<LogEntry>, DomainError>(new LogMonitorError("Log file path cannot be empty."));
        }

        var platform = activeTarget != null ? platformRegistry.GetPlatformById(activeTarget.PlatformId) : null;
        if (platform?.RuntimeLogs != null)
        {
            var res = await platform.RuntimeLogs.ReadRecentLogsAsync(logFilePath, maxLines, ct).ConfigureAwait(false);
            return res.IsSuccess
                ? Result.Ok<IReadOnlyList<LogEntry>, DomainError>(res.Value)
                : Result.Fail<IReadOnlyList<LogEntry>, DomainError>(res.Error);
        }

        foreach (var candidate in platformRegistry.GetAllPlatforms())
        {
            if (candidate.RuntimeLogs != null)
            {
                var res = await candidate.RuntimeLogs.ReadRecentLogsAsync(logFilePath, maxLines, ct).ConfigureAwait(false);
                return res.IsSuccess
                    ? Result.Ok<IReadOnlyList<LogEntry>, DomainError>(res.Value)
                    : Result.Fail<IReadOnlyList<LogEntry>, DomainError>(res.Error);
            }
        }

        return Result.Fail<IReadOnlyList<LogEntry>, DomainError>(new LogMonitorError("No log reader capability available."));
    }
}
