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
using Candied.Core.Ports;
using Candied.Decompiler.Internal.Assets;
using Candied.Primitives;

namespace Candied.Decompiler.Adapters;

internal sealed class AssetsToolsAssetExtractorAdapter : IAssetExtractor
{
    private static readonly byte[] EmbeddedClassData;

    static AssetsToolsAssetExtractorAdapter()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Candied.Decompiler.Resources.classdata.tpk")
            ?? throw new InvalidOperationException("Embedded resource 'Candied.Decompiler.Resources.classdata.tpk' was not found.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        EmbeddedClassData = ms.ToArray();
    }

    public Task<Result<IReadOnlyList<DiscoveredAssetInfo>, AssetExtractionError>> DiscoverAssetsAsync(
        GameEnvironment game,
        AssetDiscoveryFilter filter,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var discovered = new List<DiscoveredAssetInfo>();
                var am = CreateAssetsManager();

                var containers = UnityAssetContainerScanner.CollectAssetContainers(game);
                if (!string.IsNullOrWhiteSpace(filter.ContainerName))
                {
                    containers = containers.FindAll(c => Path.GetFileName(c).Contains(filter.ContainerName, StringComparison.OrdinalIgnoreCase));
                }

                var searchPattern = filter.SearchPattern?.Trim();
                var matchAllTypes = string.IsNullOrWhiteSpace(filter.AssetType) || filter.AssetType.Equals("All", StringComparison.OrdinalIgnoreCase);
                var wantSprites = matchAllTypes || string.Equals(filter.AssetType, "Sprite", StringComparison.OrdinalIgnoreCase);
                var wantTextures = matchAllTypes || string.Equals(filter.AssetType, "Texture2D", StringComparison.OrdinalIgnoreCase);
                var wantTextAssets = matchAllTypes || string.Equals(filter.AssetType, "TextAsset", StringComparison.OrdinalIgnoreCase);
                var wantMonoBehaviours = matchAllTypes || string.Equals(filter.AssetType, "MonoBehaviour", StringComparison.OrdinalIgnoreCase);

                UnityAssetContainerScanner.ProcessContainers(am, containers, (inst, containerName, containerPath) =>
                {
                    if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) return;

                    // 1. Sprites
                    if (wantSprites && discovered.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.Sprite))
                        {
                            if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) break;
                            if (filter.PathId.HasValue && inf.PathId != filter.PathId.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                var name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                var rect = baseField["m_Rect"];
                                int width = (int)rect["width"].AsFloat;
                                int height = (int)rect["height"].AsFloat;

                                discovered.Add(new DiscoveredAssetInfo(
                                    Name: name,
                                    AssetType: "Sprite",
                                    SourceContainer: containerName,
                                    SourceContainerPath: containerPath,
                                    PathId: inf.PathId,
                                    Width: width,
                                    Height: height,
                                    ByteSize: inf.ByteSize,
                                    Extension: "png"));
                            }
                            catch
                            {
                                // Skip unparseable asset
                            }
                        }
                    }

                    // 2. Texture2D
                    if (wantTextures && discovered.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.Texture2D))
                        {
                            if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) break;
                            if (filter.PathId.HasValue && inf.PathId != filter.PathId.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                var name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                int width = baseField["m_Width"].AsInt;
                                int height = baseField["m_Height"].AsInt;

                                discovered.Add(new DiscoveredAssetInfo(
                                    Name: name,
                                    AssetType: "Texture2D",
                                    SourceContainer: containerName,
                                    SourceContainerPath: containerPath,
                                    PathId: inf.PathId,
                                    Width: width,
                                    Height: height,
                                    ByteSize: inf.ByteSize,
                                    Extension: "png"));
                            }
                            catch
                            {
                                // Skip unparseable asset
                            }
                        }
                    }

                    // 3. TextAsset
                    if (wantTextAssets && discovered.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.TextAsset))
                        {
                            if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) break;
                            if (filter.PathId.HasValue && inf.PathId != filter.PathId.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                var name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                discovered.Add(new DiscoveredAssetInfo(
                                    Name: name,
                                    AssetType: "TextAsset",
                                    SourceContainer: containerName,
                                    SourceContainerPath: containerPath,
                                    PathId: inf.PathId,
                                    Width: 0,
                                    Height: 0,
                                    ByteSize: inf.ByteSize,
                                    Extension: "txt"));
                            }
                            catch
                            {
                                // Skip unparseable asset
                            }
                        }
                    }

                    // 4. MonoBehaviour / ScriptableObject
                    if (wantMonoBehaviours && discovered.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                        {
                            if (ct.IsCancellationRequested || discovered.Count >= filter.MaxResults) break;
                            if (filter.PathId.HasValue && inf.PathId != filter.PathId.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                var name = baseField["m_Name"]?.AsString;
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
                                    PathId: inf.PathId,
                                    Width: 0,
                                    Height: 0,
                                    ByteSize: inf.ByteSize,
                                    Extension: "json"));
                            }
                            catch
                            {
                                // Skip unparseable asset
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
                    new AssetExtractionError($"Failed to discover game assets: {ex.Message}"));
            }
        }, ct);
    }

    public Task<Result<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>> ExtractAssetsAsync(
        GameEnvironment game,
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

                var containers = UnityAssetContainerScanner.CollectAssetContainers(game);
                if (!string.IsNullOrWhiteSpace(filter.ContainerName))
                {
                    containers = containers.FindAll(c => Path.GetFileName(c).Contains(filter.ContainerName, StringComparison.OrdinalIgnoreCase));
                }

                var searchPattern = filter.SearchPattern?.Trim();
                var matchAllTypes = string.IsNullOrWhiteSpace(filter.AssetType) || filter.AssetType.Equals("All", StringComparison.OrdinalIgnoreCase);
                var wantSprites = matchAllTypes || string.Equals(filter.AssetType, "Sprite", StringComparison.OrdinalIgnoreCase);
                var wantTextures = matchAllTypes || string.Equals(filter.AssetType, "Texture2D", StringComparison.OrdinalIgnoreCase);
                var wantTextAssets = matchAllTypes || string.Equals(filter.AssetType, "TextAsset", StringComparison.OrdinalIgnoreCase);
                var wantMonoBehaviours = matchAllTypes || string.Equals(filter.AssetType, "MonoBehaviour", StringComparison.OrdinalIgnoreCase);

                UnityAssetContainerScanner.ProcessContainers(am, containers, (inst, containerName, containerPath) =>
                {
                    if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) return;

                    // 1. Textures
                    if (wantTextures && extracted.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.Texture2D))
                        {
                            if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) break;
                            if (filter.PathId.HasValue && inf.PathId != filter.PathId.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                var name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                var info = UnityMultiTypeAssetExtractor.ExtractTexture2D(
                                    am, inst, inf, outputDirectory, filter.DisambiguateCollisions, containerName);
                                if (info != null)
                                {
                                    extracted.Add(info);
                                }
                            }
                            catch
                            {
                                // Skip decode error
                            }
                        }
                    }

                    // 2. Sprites
                    if (wantSprites && extracted.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.Sprite))
                        {
                            if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) break;
                            if (filter.PathId.HasValue && inf.PathId != filter.PathId.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                var name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                var info = UnityMultiTypeAssetExtractor.ExtractSprite(
                                    am, inst, inf, outputDirectory, filter.DisambiguateCollisions, containerName);
                                if (info != null)
                                {
                                    extracted.Add(info);
                                }
                            }
                            catch
                            {
                                // Skip decode error
                            }
                        }
                    }

                    // 3. TextAsset
                    if (wantTextAssets && extracted.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.TextAsset))
                        {
                            if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) break;
                            if (filter.PathId.HasValue && inf.PathId != filter.PathId.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                var name = baseField["m_Name"].AsString;
                                if (string.IsNullOrWhiteSpace(name) || !MatchesPattern(name, searchPattern)) continue;

                                var info = UnityMultiTypeAssetExtractor.ExtractTextAsset(
                                    am, inst, inf, outputDirectory, filter.DisambiguateCollisions, containerName);
                                if (info != null)
                                {
                                    extracted.Add(info);
                                }
                            }
                            catch
                            {
                                // Skip decode error
                            }
                        }
                    }

                    // 4. MonoBehaviour
                    if (wantMonoBehaviours && extracted.Count < filter.MaxResults)
                    {
                        foreach (var inf in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                        {
                            if (ct.IsCancellationRequested || extracted.Count >= filter.MaxResults) break;
                            if (filter.PathId.HasValue && inf.PathId != filter.PathId.Value) continue;

                            try
                            {
                                var baseField = am.GetBaseField(inst, inf);
                                var name = baseField["m_Name"]?.AsString;
                                if (string.IsNullOrWhiteSpace(name))
                                {
                                    name = $"MonoBehaviour_{inf.PathId}";
                                }
                                if (!MatchesPattern(name, searchPattern)) continue;

                                var info = UnityMultiTypeAssetExtractor.ExtractMonoBehaviour(
                                    am, inst, inf, outputDirectory, filter.DisambiguateCollisions, containerName);
                                if (info != null)
                                {
                                    extracted.Add(info);
                                }
                            }
                            catch
                            {
                                // Skip decode error
                            }
                        }
                    }
                }, ct);

                am.UnloadAll();
                return Result.Ok<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(extracted);
            }
            catch (Exception ex)
            {
                return Result.Fail<IReadOnlyList<ExtractedAssetInfo>, AssetExtractionError>(
                    new AssetExtractionError($"Failed to extract game assets: {ex.Message}"));
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
