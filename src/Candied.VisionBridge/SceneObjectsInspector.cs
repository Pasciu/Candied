using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Candied.VisionBridge.Protocol;
using UnityEngine;

namespace Candied.VisionBridge;

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

    public static VisionSceneObjectsResponse Inspect(
        string? nameFilter,
        bool includeInactive,
        bool visibleOnly,
        string? sortingLayer,
        int maxResults)
    {
        int effectiveMax = maxResults <= 0 ? 100 : (maxResults > 1000 ? 1000 : maxResults);
        string? trimmedNameFilter = NormalizeFilter(nameFilter);
        string? trimmedSortingLayer = NormalizeFilter(sortingLayer);

        var camList = new List<VisionSceneCameraDto>();
        var renList = new List<VisionSceneRendererDto>();
        var seenCams = new HashSet<int>();
        var seenRens = new HashSet<int>();
        int totalMatched = 0;

        // 1. Enumerate cameras across loaded scenes
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
        catch { }

        // 2. Enumerate concrete Unity renderer types across loaded scenes
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
                    if (!seenRens.Add(r.GetInstanceID())) continue;

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
                    catch { }

                    if (trimmedSortingLayer != null &&
                        !string.Equals(sortingLayerName, trimmedSortingLayer, StringComparison.OrdinalIgnoreCase))
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
                    if (renList.Count >= effectiveMax)
                    {
                        continue;
                    }

                    renList.Add(BuildRendererDto(
                        r,
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
            catch { }
        }

        CollectRenderers<SpriteRenderer>();
        CollectRenderers<MeshRenderer>();
        CollectRenderers<SkinnedMeshRenderer>();
        CollectRenderers<ParticleSystemRenderer>();
        CollectRenderers<LineRenderer>();
        CollectRenderers<TrailRenderer>();

        return new VisionSceneObjectsResponse
        {
            Success = true,
            TotalCameras = camList.Count,
            TotalRenderers = renList.Count,
            TotalMatched = totalMatched,
            Truncated = totalMatched > renList.Count,
            Cameras = camList,
            Renderers = renList,
            Message = totalMatched > renList.Count
                ? "Scene objects enumerated (truncated to maxResults=" + effectiveMax + " of " + totalMatched + " matches)."
                : "Scene objects enumerated successfully."
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
        catch { }
    }

    private static VisionSceneRendererDto BuildRendererDto(
        Renderer r,
        string goName,
        string? parentName,
        string fullPath,
        string sortingLayerName,
        int sortingLayerId,
        int sortingOrder,
        string? spriteName,
        float[]? spritePivot)
    {
        var mats = new List<VisionSceneMaterialDto>();
        try
        {
            var sharedMats = r.sharedMaterials;
            if (sharedMats != null)
            {
                foreach (var m in sharedMats)
                {
                    if (m == null) continue;
                    mats.Add(new VisionSceneMaterialDto
                    {
                        MaterialName = m.name,
                        ShaderName = m.shader != null ? m.shader.name : string.Empty
                    });
                }
            }
        }
        catch { }

        Vector3 center = Vector3.zero;
        Vector3 extents = Vector3.zero;
        try
        {
            var bounds = r.bounds;
            center = bounds.center;
            extents = bounds.extents;
        }
        catch { }

        Vector3 localPos = Vector3.zero;
        Vector3 localScale = Vector3.one;
        try
        {
            localPos = r.transform.localPosition;
            localScale = r.transform.localScale;
        }
        catch { }

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
        catch { }

        return new VisionSceneRendererDto
        {
            Name = goName,
            ParentName = parentName,
            FullPath = fullPath,
            ActiveSelf = r.gameObject.activeSelf,
            ActiveInHierarchy = r.gameObject.activeInHierarchy,
            RendererType = r.GetType().Name,
            SortingLayerName = sortingLayerName,
            SortingLayerId = sortingLayerId,
            SortingOrder = sortingOrder,
            LocalPosition = new[] { localPos.x, localPos.y, localPos.z },
            LocalScale = new[] { localScale.x, localScale.y, localScale.z },
            Center = new[] { center.x, center.y, center.z },
            Extents = new[] { extents.x, extents.y, extents.z },
            SpriteName = spriteName,
            SpritePivot = spritePivot,
            Components = componentNames,
            Materials = mats,
            IsVisible = r.isVisible
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
            if (spriteObj == null) return;

            var unityObj = spriteObj as UnityEngine.Object;
            if (unityObj == null) return;

            spriteName = unityObj.name;

            Type spriteType = spriteObj.GetType();
            PropertyInfo? pivotProp = spriteType.GetProperty("pivot", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo? rectProp = spriteType.GetProperty("rect", BindingFlags.Public | BindingFlags.Instance);
            if (pivotProp != null && rectProp != null)
            {
                object? pivotVal = pivotProp.GetValue(spriteObj, null);
                object? rectVal = rectProp.GetValue(spriteObj, null);
                if (pivotVal is Vector2 pivot && rectVal is Rect rect && rect.width > 0f && rect.height > 0f)
                {
                    spritePivot = new[] { pivot.x / rect.width, pivot.y / rect.height };
                }
            }
        }
        catch { }
    }

    private static bool IsSceneGameObject(GameObject go)
    {
        if (go == null) return false;

        try
        {
            if (GameObjectSceneProperty != null && SceneIsValidMethod != null)
            {
                object? sceneObj = GameObjectSceneProperty.GetValue(go, null);
                if (sceneObj != null)
                {
                    object? validObj = SceneIsValidMethod.Invoke(sceneObj, null);
                    if (validObj is bool isValid)
                    {
                        return isValid;
                    }
                }
            }
        }
        catch { }

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
