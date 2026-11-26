using System;
using System.Collections.Generic;
using System.Reflection;
using Candied.Vision.Protocol;
using UnityEngine;

namespace Candied.VisionSensors.UnityMono;

public sealed class UguiProvider : IUiProvider
{
    private static readonly Vector3[] CornerBuffer = new Vector3[4];

    public IList<VisionUiElementDto> GetInteractiveElements()
    {
        var result = new List<VisionUiElementDto>();
        int screenWidth = Screen.width;
        int screenHeight = Screen.height;

        Type? selectableType = CapabilitiesDetector.FindType("UnityEngine.UI.Selectable");
        if (selectableType != null)
        {
            UnityEngine.Object[] selectables = UnityEngine.Object.FindObjectsOfType(selectableType);
            if (selectables != null && selectables.Length > 0)
            {
                foreach (var obj in selectables)
                {
                    if (obj is not Component comp || comp == null) continue;
                    var go = comp.gameObject;
                    if (go == null) continue;

                    bool activeInHierarchy = go.activeInHierarchy;
                    string id = BuildHierarchyPath(go.transform);
                    string typeName = comp.GetType().Name;
                    bool interactable = ReadPropertyBool(comp, "interactable", true);
                    string text = ExtractElementText(go);

                    VisionRectDto pixelRect;
                    VisionRectDto normRect;

                    if (comp.transform is RectTransform rt)
                    {
                        pixelRect = CalculateRect(rt, comp, screenHeight);
                        normRect = CoordinateConverter.ToNormalizedRect(pixelRect, screenWidth, screenHeight);
                    }
                    else
                    {
                        pixelRect = new VisionRectDto(0, 0, 0, 0);
                        normRect = new VisionRectDto(0, 0, 0, 0);
                    }

                    bool visible = activeInHierarchy && pixelRect.Width > 0 && pixelRect.Height > 0
                        && (pixelRect.X + pixelRect.Width >= 0 && pixelRect.X <= screenWidth)
                        && (pixelRect.Y + pixelRect.Height >= 0 && pixelRect.Y <= screenHeight);

                    result.Add(new VisionUiElementDto
                    {
                        Id = id,
                        Type = typeName,
                        Text = text,
                        PixelRect = pixelRect,
                        NormalizedRect = normRect,
                        Interactable = interactable,
                        Visible = visible
                    });
                }
            }
        }

        return result;
    }

    public static string BuildHierarchyPath(Transform? transform)
    {
        if (transform == null) return string.Empty;

        var parts = new List<string>();
        Transform? current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    private static VisionRectDto CalculateRect(RectTransform rt, Component comp, int screenHeight)
    {
        try
        {
            rt.GetWorldCorners(CornerBuffer);
            Camera? cam = null;
            Canvas? canvas = comp.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = canvas.worldCamera ?? Camera.main;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int i = 0; i < 4; i++)
            {
                Vector3 worldPt = CornerBuffer[i];
                Vector3 screenPt = cam != null ? cam.WorldToScreenPoint(worldPt) : worldPt;

                if (screenPt.x < minX) minX = screenPt.x;
                if (screenPt.x > maxX) maxX = screenPt.x;
                if (screenPt.y < minY) minY = screenPt.y;
                if (screenPt.y > maxY) maxY = screenPt.y;
            }

            float width = Math.Max(0, maxX - minX);
            float height = Math.Max(0, maxY - minY);

            return CoordinateConverter.BottomLeftToImagePixelRect(minX, minY, width, height, screenHeight);
        }
        catch
        {
            return new VisionRectDto(0, 0, 0, 0);
        }
    }

    public static string ExtractElementText(GameObject go)
    {
        string text = GetTextFromComponents(go);
        if (!string.IsNullOrEmpty(text)) return text;

        for (int i = 0; i < go.transform.childCount; i++)
        {
            var childGo = go.transform.GetChild(i).gameObject;
            text = GetTextFromComponents(childGo);
            if (!string.IsNullOrEmpty(text)) return text;
        }

        return string.Empty;
    }

    private static string GetTextFromComponents(GameObject go)
    {
        Component[] components = go.GetComponents<Component>();
        foreach (var c in components)
        {
            if (c == null) continue;
            Type t = c.GetType();
            if (t.Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string val = ReadPropertyString(c, "text");
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }

        return string.Empty;
    }

    private static bool ReadPropertyBool(object target, string propertyName, bool defaultValue)
    {
        try
        {
            PropertyInfo? prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanRead && prop.GetValue(target, null) is bool b)
            {
                return b;
            }
        }
        catch
        {
        }

        return defaultValue;
    }

    private static string ReadPropertyString(object target, string propertyName)
    {
        try
        {
            PropertyInfo? prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanRead)
            {
                return prop.GetValue(target, null)?.ToString() ?? string.Empty;
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}
