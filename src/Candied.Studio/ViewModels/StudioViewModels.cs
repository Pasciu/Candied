using System;
using System.Collections.Generic;
using System.Globalization;
using Candied.Studio.Services;
using Candied.VisionBridge.Protocol;

namespace Candied.Studio.ViewModels;

public sealed record ViewportPresetOption(string Key, string DisplayName)
{
    public static IReadOnlyList<ViewportPresetOption> DefaultPresets { get; } = new[]
    {
        new ViewportPresetOption("auto_16_9", "Auto-Fit 16:9 (Proporzioni 1080p)"),
        new ViewportPresetOption("960x540", "960×540 (qHD Compact • 50% 1080p)"),
        new ViewportPresetOption("1280x720", "1280×720 (HD 16:9 • 67% 1080p)"),
        new ViewportPresetOption("1920x1080", "1920×1080 (Full HD 16:9)")
    };
}

public sealed record StudioLogEntry(
    string Timestamp,
    string Message,
    string FullText,
    string Level,
    string ForegroundHex)
{
    public bool IsWarningOrError => string.Equals(Level, "Error", StringComparison.Ordinal) ||
                                    string.Equals(Level, "Warning", StringComparison.Ordinal);

    public static StudioLogEntry Create(string rawMessage, DateTime timestamp)
    {
        string msg = rawMessage ?? string.Empty;
        string timeStr = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        string full = $"[{timeStr}] {msg}";

        string level = ClassifyLevel(msg);
        string color = level switch
        {
            "Error" => "#F87171",
            "Warning" => "#FBBF24",
            "Bridge" => "#38BDF8",
            _ => "#E4E4E7"
        };

        return new StudioLogEntry(timeStr, msg, full, level, color);
    }

    public static string ClassifyLevel(string message)
    {
        if (string.IsNullOrEmpty(message)) return "Info";

        if (message.Contains("[Error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("[Fatal", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Errore", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("fallit", StringComparison.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (message.Contains("[Warning", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ATTENZIONE", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        if (message.Contains("[VisionBridge]", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("[Candied.VisionBridge]", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("[Host]", StringComparison.OrdinalIgnoreCase))
        {
            return "Bridge";
        }

        return "Info";
    }

    public bool MatchesFilter(string? searchText, bool onlyWarningsAndErrors)
    {
        if (onlyWarningsAndErrors && !IsWarningOrError)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            return FullText.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}

public sealed record UiElementItemViewModel(
    string Id,
    string Type,
    string DisplayLabel,
    string CoordinatesSummary,
    string Ref1080Summary,
    float CenterX,
    float CenterY,
    bool Interactable,
    bool Visible)
{
    public static UiElementItemViewModel FromDto(VisionUiElementDto dto, int frameWidth, int frameHeight)
    {
        float cx = dto.PixelRect.X + (dto.PixelRect.Width * 0.5f);
        float cy = dto.PixelRect.Y + (dto.PixelRect.Height * 0.5f);

        var (refX, refY) = ViewportCalculator.MapToReference1080p(
            (int)Math.Round(cx),
            (int)Math.Round(cy),
            frameWidth > 0 ? frameWidth : ViewportCalculator.ReferenceDesignWidth,
            frameHeight > 0 ? frameHeight : ViewportCalculator.ReferenceDesignHeight);

        string label = string.IsNullOrWhiteSpace(dto.Text) ? dto.Id : $"{dto.Text} ({dto.Id})";
        string coords = $"Frame: ({cx:F0}, {cy:F0}) [{dto.PixelRect.Width:F0}×{dto.PixelRect.Height:F0}]";
        string refCoords = $"1080p: ({refX}, {refY})";

        return new UiElementItemViewModel(
            dto.Id,
            dto.Type,
            label,
            coords,
            refCoords,
            cx,
            cy,
            dto.Interactable,
            dto.Visible);
    }
}

public sealed record SceneCameraItemViewModel(
    string Name,
    string ProjectionSummary,
    string PositionSummary,
    string DepthSummary)
{
    public static SceneCameraItemViewModel FromDto(VisionSceneCameraDto dto)
    {
        string proj = dto.Orthographic
            ? $"Ortho (Size {dto.OrthographicSize:F2})"
            : "Perspective";

        float px = dto.Position != null && dto.Position.Length > 0 ? dto.Position[0] : 0f;
        float py = dto.Position != null && dto.Position.Length > 1 ? dto.Position[1] : 0f;
        float pz = dto.Position != null && dto.Position.Length > 2 ? dto.Position[2] : 0f;

        return new SceneCameraItemViewModel(
            dto.Name,
            proj,
            $"Pos: ({px:F1}, {py:F1}, {pz:F1})",
            $"Depth: {dto.Depth:F0}");
    }
}

public sealed record SceneRendererItemViewModel(
    string Name,
    string HierarchyDisplay,
    string RendererType,
    string SortingSummary,
    string BoundsSummary,
    string ShaderSummary)
{
    public static SceneRendererItemViewModel FromDto(VisionSceneRendererDto dto)
    {
        string hierarchy = string.IsNullOrWhiteSpace(dto.ParentName)
            ? dto.Name
            : $"{dto.ParentName} / {dto.Name}";

        string layerName = string.IsNullOrWhiteSpace(dto.SortingLayerName) ? "Default" : dto.SortingLayerName;
        string sorting = $"Layer: {layerName} (Order {dto.SortingOrder})";

        float cx = dto.Center != null && dto.Center.Length > 0 ? dto.Center[0] : 0f;
        float cy = dto.Center != null && dto.Center.Length > 1 ? dto.Center[1] : 0f;
        float ex = dto.Extents != null && dto.Extents.Length > 0 ? dto.Extents[0] : 0f;
        float ey = dto.Extents != null && dto.Extents.Length > 1 ? dto.Extents[1] : 0f;

        string bounds = $"Center: ({cx:F2}, {cy:F2}) • Size: ({ex * 2:F2}×{ey * 2:F2})";

        string shader = dto.Materials != null && dto.Materials.Count > 0
            ? $"{dto.Materials[0].MaterialName} [{dto.Materials[0].ShaderName}]"
            : "-";

        return new SceneRendererItemViewModel(
            dto.Name,
            hierarchy,
            dto.RendererType,
            sorting,
            bounds,
            shader);
    }
}
