using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Candied.Vision.Controls;
using Candied.Vision.Hosting;
using Candied.Vision.Protocol;

namespace Candied.Studio.ViewModels;

public sealed record ViewportPresetOption(string Key, string DisplayName)
{
    public static IReadOnlyList<ViewportPresetOption> DefaultPresets { get; } =
    [
        new ViewportPresetOption("auto_16_9", "Auto-Fit 16:9 (Proporzioni 1080p)"),
        new ViewportPresetOption("960x540", "960×540 (qHD Compact • 50% 1080p)"),
        new ViewportPresetOption("1280x720", "1280×720 (HD 16:9 • 67% 1080p)"),
        new ViewportPresetOption("1920x1080", "1920×1080 (Full HD 16:9)")
    ];
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
        if (string.IsNullOrEmpty(message))
        {
            return "Info";
        }

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
            message.Contains("[SensorBridge]", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("[Candied.Vision", StringComparison.OrdinalIgnoreCase) ||
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
        ArgumentNullException.ThrowIfNull(dto);

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
        ArgumentNullException.ThrowIfNull(dto);

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

public sealed record SceneNodeItemViewModel(
    string Name,
    string HierarchyDisplay,
    string NodeType,
    string ComponentsSummary,
    string BoundsSummary,
    string AttributesSummary)
{
    public static SceneNodeItemViewModel FromDto(VisionSceneNodeDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        string hierarchy = !string.IsNullOrWhiteSpace(dto.FullPath)
            ? dto.FullPath
            : (string.IsNullOrWhiteSpace(dto.ParentName) ? dto.Name : $"{dto.ParentName} / {dto.Name}");

        string comps = dto.Components != null && dto.Components.Count > 0
            ? string.Join(", ", dto.Components.Take(4))
            : dto.NodeType;

        float cx = dto.BoundsCenter != null && dto.BoundsCenter.Length > 0 ? dto.BoundsCenter[0] : 0f;
        float cy = dto.BoundsCenter != null && dto.BoundsCenter.Length > 1 ? dto.BoundsCenter[1] : 0f;
        float ex = dto.BoundsExtents != null && dto.BoundsExtents.Length > 0 ? dto.BoundsExtents[0] : 0f;
        float ey = dto.BoundsExtents != null && dto.BoundsExtents.Length > 1 ? dto.BoundsExtents[1] : 0f;

        string bounds = $"Center: ({cx:F2}, {cy:F2}) • Size: ({ex * 2:F2}×{ey * 2:F2})";

        string attrs = dto.Attributes != null && dto.Attributes.Count > 0
            ? string.Join(" • ", dto.Attributes.Take(4).Select(kv => $"{kv.Key}: {kv.Value}"))
            : "-";

        return new SceneNodeItemViewModel(
            dto.Name,
            hierarchy,
            dto.NodeType,
            comps,
            bounds,
            attrs);
    }
}

public sealed record ControlActionItemViewModel(
    string ActionName,
    string DisplayName,
    string Source,
    string BindingsSummary)
{
    public static ControlActionItemViewModel FromEntry(ControlActionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string summary = entry.Bindings.Count == 0
            ? "(Nessun binding)"
            : string.Join(" | ", entry.Bindings.Select(FormatBinding));

        return new ControlActionItemViewModel(
            entry.ActionName,
            entry.DisplayName,
            entry.Source,
            summary);
    }

    private static string FormatBinding(InputBinding b)
    {
        return b.Channel switch
        {
            InputChannel.Keyboard => $"Key: {b.Key}",
            InputChannel.Gamepad => $"Pad: {b.GamepadButton}",
            _ => b.NormalizedX.HasValue && b.NormalizedY.HasValue
                ? $"Mouse({b.MouseButton}) @ ({b.NormalizedX.Value:F2}, {b.NormalizedY.Value:F2})"
                : $"Mouse({b.MouseButton})"
        };
    }
}
