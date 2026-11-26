using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Platforms.Unity.Shared.Contracts;
using Candied.Platforms.Unity.Shared.Internal.Assets;
using Candied.Primitives;

namespace Candied.Platforms.Unity.Shared.Internal;

internal sealed class UnityAssetsPipelineCapability : IUnityAssetPipeline
{
    private static readonly byte[] EmbeddedClassData = LoadEmbeddedClassData();

    private static byte[] LoadEmbeddedClassData()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Candied.Platforms.Unity.Shared.Resources.classdata.tpk")
            ?? throw new InvalidOperationException("Embedded resource 'Candied.Platforms.Unity.Shared.Resources.classdata.tpk' was not found.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public Task<Result<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>> DiscoverAssetsAsync(
        TargetDescriptor target,
        AssetDiscoveryFilter filter,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var discovered = new List<DiscoveredAssetInfo>();
                var am = CreateAssetsManager();

                var containers = UnityAssetContainerScanner.CollectAssetContainers(target);
                if (!string.IsNullOrWhiteSpace(filter.ContainerName))
                {
                    containers = containers.FindAll(c => Path.GetFileName(c).Contains(filter.ContainerName, StringComparison.OrdinalIgnoreCase));
                }

                string? searchPattern = filter.SearchPattern?.Trim();
                bool matchAllTypes = string.IsNullOrWhiteSpace(filter.AssetType) || filter.AssetType.Equals("All", StringComparison.OrdinalIgnoreCase);
                bool wantSprites = matchAllTypes || string.Equals(filter.AssetType, "Sprite", StringComparison.OrdinalIgnoreCase);
                bool wantTextures = matchAllTypes || string.Equals(filter.AssetType, "Texture2D", StringComparison.OrdinalIgnoreCase);
                bool wantTextAssets = matchAllTypes || string.Equals(filter.AssetType, "TextAsset", StringComparison.OrdinalIgnoreCase);
                bool wantMonoBehaviours = matchAllTypes || string.Equals(filter.AssetType, "MonoBehaviour", StringComparison.OrdinalIgnoreCase);

                UnityAssetContainerScanner.ProcessContainers(am, containers, (inst, containerName, containerPath) =>
                {
                    if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) return;

                    if (wantSprites && discovered.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.Sprite))
                        {
                            if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) break;
                            if (filter.AssetKey.HasValue && inf.PathId != filter.AssetKey.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                string name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                var rect = baseField["m_Rect"];
                                discovered.Add(new DiscoveredAssetInfo(
                                    Name: name,
                                    AssetType: "Sprite",
                                    SourceContainer: containerName,
                                    SourceContainerPath: containerPath,
                                    AssetKey: inf.PathId,
                                    Width: (int)rect["width"].AsFloat,
                                    Height: (int)rect["height"].AsFloat,
                                    ByteSize: inf.ByteSize,
                                    Extension: "png"));
                            }
                            catch
                            {
                            }
                        }
                    }

                    if (wantTextures && discovered.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.Texture2D))
                        {
                            if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) break;
                            if (filter.AssetKey.HasValue && inf.PathId != filter.AssetKey.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                string name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                discovered.Add(new DiscoveredAssetInfo(
                                    Name: name,
                                    AssetType: "Texture2D",
                                    SourceContainer: containerName,
                                    SourceContainerPath: containerPath,
                                    AssetKey: inf.PathId,
                                    Width: baseField["m_Width"].AsInt,
                                    Height: baseField["m_Height"].AsInt,
                                    ByteSize: inf.ByteSize,
                                    Extension: "png"));
                            }
                            catch
                            {
                            }
                        }
                    }

                    if (wantTextAssets && discovered.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.TextAsset))
                        {
                            if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) break;
                            if (filter.AssetKey.HasValue && inf.PathId != filter.AssetKey.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                string name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                discovered.Add(new DiscoveredAssetInfo(
                                    Name: name,
                                    AssetType: "TextAsset",
                                    SourceContainer: containerName,
                                    SourceContainerPath: containerPath,
                                    AssetKey: inf.PathId,
                                    Width: 0,
                                    Height: 0,
                                    ByteSize: inf.ByteSize,
                                    Extension: "txt"));
                            }
                            catch
                            {
                            }
                        }
                    }

                    if (wantMonoBehaviours && discovered.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                        {
                            if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) break;
                            if (filter.AssetKey.HasValue && inf.PathId != filter.AssetKey.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                string? name = baseField["m_Name"]?.AsString;
                                if (string.IsNullOrWhiteSpace(name))
                                {
                                    name = $"MonoBehaviour_{inf.PathId}";
                                }

                                if (!MatchesPattern(name, searchPattern)) continue;

                                discovered.Add(new DiscoveredAssetInfo(
                                    Name: name,
                                    AssetType: "MonoBehaviour",
                                    SourceContainer: containerName,
                                    SourceContainerPath: containerPath,
                                    AssetKey: inf.PathId,
                                    Width: 0,
                                    Height: 0,
                                    ByteSize: inf.ByteSize,
                                    Extension: "json"));
                            }
                            catch
                            {
                            }
                        }
                    }
                }, ct);

                am.UnloadAll();
                return Result.Ok<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>(discovered);
            }
            catch (Exception ex)
            {
                return Result.Fail<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>(
                    new AssetExtractionError($"Failed to discover Unity assets: {ex.Message}"));
            }
        }, ct);
    }

    public Task<Result<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>> ExtractAssetsAsync(
        TargetDescriptor target,
        AssetExtractionFilter filter,
        string outputDirectory,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                var extracted = new List<ExtractedAssetInfo>();
                var am = CreateAssetsManager();

                var containers = UnityAssetContainerScanner.CollectAssetContainers(target);
                if (!string.IsNullOrWhiteSpace(filter.ContainerName))
                {
                    containers = containers.FindAll(c => Path.GetFileName(c).Contains(filter.ContainerName, StringComparison.OrdinalIgnoreCase));
                }

                string? searchPattern = filter.SearchPattern?.Trim();
                bool matchAllTypes = string.IsNullOrWhiteSpace(filter.AssetType) || filter.AssetType.Equals("All", StringComparison.OrdinalIgnoreCase);
                bool wantSprites = matchAllTypes || string.Equals(filter.AssetType, "Sprite", StringComparison.OrdinalIgnoreCase);
                bool wantTextures = matchAllTypes || string.Equals(filter.AssetType, "Texture2D", StringComparison.OrdinalIgnoreCase);
                bool wantTextAssets = matchAllTypes || string.Equals(filter.AssetType, "TextAsset", StringComparison.OrdinalIgnoreCase);
                bool wantMonoBehaviours = matchAllTypes || string.Equals(filter.AssetType, "MonoBehaviour", StringComparison.OrdinalIgnoreCase);

                UnityAssetContainerScanner.ProcessContainers(am, containers, (inst, containerName, _) =>
                {
                    if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) return;

                    if (wantTextures && extracted.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.Texture2D))
                        {
                            if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) break;
                            if (filter.AssetKey.HasValue && inf.PathId != filter.AssetKey.Value) continue;
                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                string name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;
                                var info = UnityMultiTypeAssetExtractor.ExtractTexture2D(am, inst, inf, outputDirectory, filter.DisambiguateCollisions, containerName);
                                if (info != null) extracted.Add(info);
                            }
                            catch { }
                        }
                    }

                    if (wantSprites && extracted.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.Sprite))
                        {
                            if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) break;
                            if (filter.AssetKey.HasValue && inf.PathId != filter.AssetKey.Value) continue;
                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                string name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;
                                var info = UnityMultiTypeAssetExtractor.ExtractSprite(am, inst, inf, outputDirectory, filter.DisambiguateCollisions, containerName);
                                if (info != null) extracted.Add(info);
                            }
                            catch { }
                        }
                    }

                    if (wantTextAssets && extracted.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.TextAsset))
                        {
                            if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) break;
                            if (filter.AssetKey.HasValue && inf.PathId != filter.AssetKey.Value) continue;
                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                string name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;
                                var info = UnityMultiTypeAssetExtractor.ExtractTextAsset(am, inst, inf, outputDirectory, filter.DisambiguateCollisions, containerName);
                                if (info != null) extracted.Add(info);
                            }
                            catch { }
                        }
                    }

                    if (wantMonoBehaviours && extracted.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                        {
                            if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) break;
                            if (filter.AssetKey.HasValue && inf.PathId != filter.AssetKey.Value) continue;
                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                string? name = baseField["m_Name"]?.AsString;
                                if (string.IsNullOrWhiteSpace(name)) name = $"MonoBehaviour_{inf.PathId}";
                                if (!MatchesPattern(name, searchPattern)) continue;
                                var info = UnityMultiTypeAssetExtractor.ExtractMonoBehaviour(am, inst, inf, outputDirectory, filter.DisambiguateCollisions, containerName);
                                if (info != null) extracted.Add(info);
                            }
                            catch { }
                        }
                    }
                }, ct);

                am.UnloadAll();
                return Result.Ok<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(extracted);
            }
            catch (Exception ex)
            {
                return Result.Fail<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(
                    new AssetExtractionError($"Failed to extract Unity assets: {ex.Message}"));
            }
        }, ct);
    }

    private static AssetsManager CreateAssetsManager()
    {
        var am = new AssetsManager();
        using var stream = new MemoryStream(EmbeddedClassData, false);
        am.LoadClassPackage(stream);
        return am;
    }

    private static bool MatchesPattern(string text, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "*")
        {
            return true;
        }

        return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
