using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Candied.Vision.Protocol;
using UnityEngine;

namespace Candied.VisionSensors.UnityMono;

internal static class SceneObjectsInspector
{
    private static readonly PropertyInfo? GameObjectSceneProperty =
        typeof(GameObject).GetProperty("scene", BindingFlags.Public | BindingFlags.Instance);

    private static readonly MethodInfo? SceneIsValidMethod =
        GameObjectSceneProperty?.PropertyType.GetMethod("IsValid", BindingFlags.Public | BindingFlags.Instance);

    private static readonly PropertyInfo? SpriteRendererSpriteProperty =
        typeof(SpriteRenderer).GetProperty("sprite", BindingFlags.Public | BindingFlags.Instance);

    public static int CountActiveRenderers()
    {
        var seen = new HashSet<int>();
        CountRenderersOfType<SpriteRenderer>(seen);
        CountRenderersOfType<MeshRenderer>(seen);
        CountRenderersOfType<SkinnedMeshRenderer>(seen);
        CountRenderersOfType<ParticleSystemRenderer>(seen);
        CountRenderersOfType<LineRenderer>(seen);
        CountRenderersOfType<TrailRenderer>(seen);
        return seen.Count;
    }

    public static VisionSceneGraphResponse Inspect(
        string? nameFilter,
        bool includeInactive,
        bool visibleOnly,
        string? attributeFilter,
        int maxResults)
    {
        int effectiveMax = maxResults <= 0 ? 100 : (maxResults > 1000 ? 1000 : maxResults);
        string? trimmedNameFilter = NormalizeFilter(nameFilter);
        string? trimmedAttrFilter = NormalizeFilter(attributeFilter);

        var camList = new List<VisionSceneCameraDto>();
        var nodeList = new List<VisionSceneNodeDto>();
        var seenCams = new HashSet<int>();
        var seenNodes = new HashSet<int>();
        int totalMatched = 0;

        try
        {
            var cams = Resources.FindObjectsOfTypeAll<Camera>();
            if (cams != null)
            {
                foreach (var cam in cams)
                {
                    if (cam == null || cam.gameObject == null) continue;
                    if (!IsSceneGameObject(cam.gameObject)) continue;
                    if (!includeInactive && (!cam.enabled || !cam.gameObject.activeInHierarchy)) continue;
                    if (!seenCams.Add(cam.GetInstanceID())) continue;

                    string camFullPath = BuildFullPath(cam.transform);
                    if (trimmedNameFilter != null &&
                        cam.name.IndexOf(trimmedNameFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        cam.gameObject.name.IndexOf(trimmedNameFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        camFullPath.IndexOf(trimmedNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    Vector3 pos = Vector3.zero;
                    try { pos = cam.transform.position; } catch { }

                    camList.Add(new VisionSceneCameraDto
                    {
                        Name = cam.name,
                        Position = new[] { pos.x, pos.y, pos.z },
                        Orthographic = cam.orthographic,
                        OrthographicSize = cam.orthographicSize,
                        CullingMask = cam.cullingMask,
                        Depth = cam.depth
                    });
                }
            }
        }
        catch
        {
        }

        void CollectRenderers<T>() where T : Renderer
        {
            try
            {
                var rens = Resources.FindObjectsOfTypeAll<T>();
                if (rens == null) return;

                foreach (var r in rens)
                {
                    if (r == null || r.gameObject == null) continue;
                    if (!IsSceneGameObject(r.gameObject)) continue;
                    if (!includeInactive && (!r.enabled || !r.gameObject.activeInHierarchy)) continue;
                    int instanceId = r.GetInstanceID();
                    if (!seenNodes.Add(instanceId)) continue;
                    if (visibleOnly && !r.isVisible) continue;

                    string sortingLayerName = string.Empty;
                    int sortingLayerId = 0;
                    int sortingOrder = 0;
                    try
                    {
                        sortingLayerName = r.sortingLayerName ?? string.Empty;
                        sortingLayerId = r.sortingLayerID;
                        sortingOrder = r.sortingOrder;
                    }
                    catch
                    {
                    }

                    if (trimmedAttrFilter != null &&
                        sortingLayerName.IndexOf(trimmedAttrFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        r.GetType().Name.IndexOf(trimmedAttrFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string goName = r.gameObject.name ?? string.Empty;
                    string? parentName = r.transform.parent != null ? r.transform.parent.name : null;
                    string fullPath = BuildFullPath(r.transform);

                    ExtractSpriteInfo(r, out string? spriteName, out float[]? spritePivot);

                    if (trimmedNameFilter != null)
                    {
                        bool matchName = goName.IndexOf(trimmedNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        bool matchPath = fullPath.IndexOf(trimmedNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        bool matchSprite = spriteName != null && spriteName.IndexOf(trimmedNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!matchName && !matchPath && !matchSprite)
                        {
                            continue;
                        }
                    }

                    totalMatched++;
                    if (nodeList.Count >= effectiveMax)
                    {
                        continue;
                    }

                    nodeList.Add(BuildSceneNodeDto(
                        r,
                        instanceId,
                        goName,
                        parentName,
                        fullPath,
                        sortingLayerName,
                        sortingLayerId,
                        sortingOrder,
                        spriteName,
                        spritePivot));
                }
            }
            catch
            {
            }
        }

        CollectRenderers<SpriteRenderer>();
        CollectRenderers<MeshRenderer>();
        CollectRenderers<SkinnedMeshRenderer>();
        CollectRenderers<ParticleSystemRenderer>();
        CollectRenderers<LineRenderer>();
        CollectRenderers<TrailRenderer>();

        return new VisionSceneGraphResponse
        {
            Success = true,
            TotalCameras = camList.Count,
            TotalNodes = nodeList.Count,
            TotalMatched = totalMatched,
            Truncated = totalMatched > nodeList.Count,
            Cameras = camList,
            Nodes = nodeList,
            Message = totalMatched > nodeList.Count
                ? "Scene nodes enumerated (truncated to maxResults=" + effectiveMax + " of " + totalMatched + " matches)."
                : "Scene nodes enumerated successfully."
        };
    }

    private static void CountRenderersOfType<T>(HashSet<int> seen) where T : Renderer
    {
        try
        {
            var rens = Resources.FindObjectsOfTypeAll<T>();
            if (rens == null) return;
            foreach (var r in rens)
            {
                if (r == null || !r.enabled || r.gameObject == null || !r.gameObject.activeInHierarchy) continue;
                if (!IsSceneGameObject(r.gameObject)) continue;
                seen.Add(r.GetInstanceID());
            }
        }
        catch
        {
        }
    }

    private static VisionSceneNodeDto BuildSceneNodeDto(
        Renderer r,
        int instanceId,
        string goName,
        string? parentName,
        string fullPath,
        string sortingLayerName,
        int sortingLayerId,
        int sortingOrder,
        string? spriteName,
        float[]? spritePivot)
    {
        var attributes = new Dictionary<string, string>
        {
            ["sortingLayerName"] = sortingLayerName,
            ["sortingLayerId"] = sortingLayerId.ToString(CultureInfo.InvariantCulture),
            ["sortingOrder"] = sortingOrder.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrEmpty(spriteName))
        {
            attributes["spriteName"] = spriteName!;
        }

        if (spritePivot != null && spritePivot.Length == 2)
        {
            attributes["spritePivot"] = spritePivot[0].ToString("F2", CultureInfo.InvariantCulture) + "," + spritePivot[1].ToString("F2", CultureInfo.InvariantCulture);
        }

        try
        {
            var sharedMats = r.sharedMaterials;
            if (sharedMats != null && sharedMats.Length > 0 && sharedMats[0] != null)
            {
                attributes["materialName"] = sharedMats[0].name ?? string.Empty;
                attributes["shaderName"] = sharedMats[0].shader != null ? (sharedMats[0].shader.name ?? string.Empty) : string.Empty;
            }
        }
        catch
        {
        }

        Vector3 center = Vector3.zero;
        Vector3 extents = Vector3.zero;
        try
        {
            var bounds = r.bounds;
            center = bounds.center;
            extents = bounds.extents;
        }
        catch
        {
        }

        Vector3 localPos = Vector3.zero;
        Vector3 localScale = Vector3.one;
        try
        {
            localPos = r.transform.localPosition;
            localScale = r.transform.localScale;
        }
        catch
        {
        }

        var componentNames = new List<string>();
        try
        {
            var components = r.gameObject.GetComponents<Component>();
            if (components != null)
            {
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    string typeName = comp.GetType().Name;
                    if (!componentNames.Contains(typeName))
                    {
                        componentNames.Add(typeName);
                    }
                }
            }
        }
        catch
        {
        }

        return new VisionSceneNodeDto
        {
            Id = instanceId.ToString(CultureInfo.InvariantCulture),
            Name = goName,
            ParentName = parentName,
            FullPath = fullPath,
            NodeType = r.GetType().Name,
            ActiveSelf = r.gameObject.activeSelf,
            ActiveInHierarchy = r.gameObject.activeInHierarchy,
            IsVisible = r.isVisible,
            LocalPosition = new[] { localPos.x, localPos.y, localPos.z },
            LocalScale = new[] { localScale.x, localScale.y, localScale.z },
            BoundsCenter = new[] { center.x, center.y, center.z },
            BoundsExtents = new[] { extents.x, extents.y, extents.z },
            Components = componentNames,
            Attributes = attributes
        };
    }

    private static void ExtractSpriteInfo(Renderer r, out string? spriteName, out float[]? spritePivot)
    {
        spriteName = null;
        spritePivot = null;

        if (!(r is SpriteRenderer) || SpriteRendererSpriteProperty == null)
        {
            return;
        }

        try
        {
            object? spriteObj = SpriteRendererSpriteProperty.GetValue(r, null);
            var unityObj = spriteObj as UnityEngine.Object;
            if (spriteObj == null || unityObj == null) return;

            spriteName = unityObj.name;
            Type spriteType = spriteObj.GetType();
            object? pivotVal = spriteType.GetProperty("pivot", BindingFlags.Public | BindingFlags.Instance)?.GetValue(spriteObj, null);
            object? rectVal = spriteType.GetProperty("rect", BindingFlags.Public | BindingFlags.Instance)?.GetValue(spriteObj, null);
            if (pivotVal is Vector2 pivot && rectVal is Rect rect && rect.width > 0f && rect.height > 0f)
            {
                spritePivot = new[] { pivot.x / rect.width, pivot.y / rect.height };
            }
        }
        catch
        {
        }
    }

    private static bool IsSceneGameObject(GameObject go)
    {
        if (go == null) return false;
        try
        {
            if (GameObjectSceneProperty != null && SceneIsValidMethod != null)
            {
                object? sceneObj = GameObjectSceneProperty.GetValue(go, null);
                if (sceneObj != null && SceneIsValidMethod.Invoke(sceneObj, null) is bool isValid)
                {
                    return isValid;
                }
            }
        }
        catch
        {
        }

        return go.hideFlags == HideFlags.None;
    }

    private static string BuildFullPath(Transform? transform)
    {
        if (transform == null) return string.Empty;

        var segments = new List<string>();
        Transform? current = transform;
        int guard = 0;
        while (current != null && guard < 64)
        {
            segments.Add(current.name ?? string.Empty);
            current = current.parent;
            guard++;
        }

        segments.Reverse();
        var sb = new StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            if (i > 0) sb.Append('/');
            sb.Append(segments[i]);
        }

        return sb.ToString();
    }

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        string trimmed = value!.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
