using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Candied.VisionBridge.Protocol;
using UnityEngine;

namespace Candied.VisionBridge;

internal static class RuntimeAssetsInspector
{
    public static VisionRuntimeAssetsResponse Inspect(
        string? nameFilter,
        string? assetType,
        int maxResults)
    {
        int effectiveMax = maxResults <= 0 ? 50 : (maxResults > 500 ? 500 : maxResults);
        string? trimmedFilter = NormalizeFilter(nameFilter);
        string requestedType = NormalizeFilter(assetType) ?? "Sprite";

        var bundles = CollectLoadedBundles(trimmedFilter);
        var loadedAssets = new List<VisionLoadedAssetDto>();
        int totalLoadedMatches = CollectLoadedAssets(requestedType, trimmedFilter, effectiveMax, loadedAssets);

        return new VisionRuntimeAssetsResponse
        {
            Success = true,
            TotalBundles = bundles.Count,
            TotalLoadedAssets = totalLoadedMatches,
            Truncated = totalLoadedMatches > loadedAssets.Count,
            Bundles = bundles,
            LoadedAssets = loadedAssets,
            Message = "Runtime assets inspected (assetType=" + requestedType + ")."
        };
    }

    private static List<VisionLoadedBundleDto> CollectLoadedBundles(string? nameFilter)
    {
        var result = new List<VisionLoadedBundleDto>();

        try
        {
            Type? assetBundleType = CapabilitiesDetector.FindType("UnityEngine.AssetBundle");
            if (assetBundleType == null)
            {
                return result;
            }

            MethodInfo? getAllMethod = assetBundleType.GetMethod(
                "GetAllLoadedAssetBundles",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (getAllMethod == null)
            {
                return result;
            }

            object? enumerableObj = getAllMethod.Invoke(null, null);
            if (!(enumerableObj is IEnumerable enumerable))
            {
                return result;
            }

            PropertyInfo? isStreamedProp = assetBundleType.GetProperty("isStreamedSceneAssetBundle", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? getAllAssetNamesMethod = assetBundleType.GetMethod("GetAllAssetNames", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

            foreach (object? bundleObj in enumerable)
            {
                if (bundleObj == null) continue;
                var unityObj = bundleObj as UnityEngine.Object;
                string bundleName = unityObj != null ? (unityObj.name ?? string.Empty) : (bundleObj.ToString() ?? string.Empty);

                bool isStreamed = false;
                try
                {
                    if (isStreamedProp != null)
                    {
                        object? val = isStreamedProp.GetValue(bundleObj, null);
                        if (val is bool b) isStreamed = b;
                    }
                }
                catch { }

                int assetCount = 0;
                var matchingPaths = new List<string>();
                if (!isStreamed && getAllAssetNamesMethod != null)
                {
                    try
                    {
                        if (getAllAssetNamesMethod.Invoke(bundleObj, null) is string[] names)
                        {
                            assetCount = names.Length;
                            if (nameFilter != null)
                            {
                                foreach (string assetPath in names)
                                {
                                    if (string.IsNullOrEmpty(assetPath)) continue;
                                    if (assetPath.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        matchingPaths.Add(assetPath);
                                        if (matchingPaths.Count >= 25) break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                result.Add(new VisionLoadedBundleDto
                {
                    Name = bundleName,
                    IsStreamedScene = isStreamed,
                    AssetCount = assetCount,
                    MatchingAssetPaths = matchingPaths
                });
            }
        }
        catch { }

        return result;
    }

    private static int CollectLoadedAssets(
        string requestedType,
        string? nameFilter,
        int maxResults,
        List<VisionLoadedAssetDto> output)
    {
        Type? targetType = ResolveUnityAssetType(requestedType);
        if (targetType == null)
        {
            return 0;
        }

        int totalMatched = 0;
        var seenIds = new HashSet<int>();

        try
        {
            UnityEngine.Object[]? found = Resources.FindObjectsOfTypeAll(targetType);
            if (found == null) return 0;

            foreach (var obj in found)
            {
                if (obj == null) continue;
                int id = obj.GetInstanceID();
                if (!seenIds.Add(id)) continue;

                string objName = obj.name ?? string.Empty;
                if (objName.Length == 0) continue;

                if (nameFilter != null &&
                    objName.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                totalMatched++;
                if (output.Count < maxResults)
                {
                    output.Add(new VisionLoadedAssetDto
                    {
                        Name = objName,
                        AssetType = obj.GetType().Name,
                        InstanceId = id
                    });
                }
            }
        }
        catch { }

        return totalMatched;
    }

    private static Type? ResolveUnityAssetType(string requestedType)
    {
        if (string.Equals(requestedType, "Sprite", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(Sprite);
        }

        if (string.Equals(requestedType, "Texture2D", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestedType, "Texture", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(Texture2D);
        }

        if (string.Equals(requestedType, "Material", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(Material);
        }

        if (string.Equals(requestedType, "GameObject", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(GameObject);
        }

        if (string.Equals(requestedType, "ScriptableObject", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(ScriptableObject);
        }

        if (string.Equals(requestedType, "Shader", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(Shader);
        }

        if (string.Equals(requestedType, "AudioClip", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilitiesDetector.FindType("UnityEngine.AudioClip");
        }

        if (string.Equals(requestedType, "All", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(UnityEngine.Object);
        }

        return CapabilitiesDetector.FindType("UnityEngine." + requestedType)
            ?? CapabilitiesDetector.FindType(requestedType);
    }

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        string trimmed = value!.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
