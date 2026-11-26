using System;
using System.Collections.Generic;

namespace Candied.Vision.Models;

public enum VisionTier
{
    None,
    BaseWindow,
    SensorBridge
}

public sealed record VisionFrame(
    byte[] PngBytes,
    bool IsUniform,
    int Width,
    int Height,
    VisionTier SourceTier = VisionTier.SensorBridge
);

public sealed record VisionError(
    string Code,
    string Message,
    string Suggestion
);

public sealed record SceneGraphQuery(
    string? NameFilter = null,
    bool IncludeInactive = false,
    bool VisibleOnly = false,
    string? AttributeFilter = null,
    int MaxResults = 100
);

public sealed record RuntimeAssetsQuery(
    string? NameFilter = null,
    string? AssetType = null,
    int MaxResults = 50
);

public sealed record WindowLaunchSpec(
    string TargetName,
    string TargetPath,
    string? RunnerPath = null,
    string? ExtraArguments = null,
    string? WorkingDirectory = null,
    string? WindowClassHint = null,
    bool SupportsParentHwndFlag = false,
    string? LogFilePath = null,
    int TargetWidth = 1280,
    int TargetHeight = 720
);

public sealed record StudioSessionInfo(
    int StudioPid,
    int StudioPort,
    string StudioToken,
    string? ActiveTargetName,
    int? TargetPid,
    int? SensorPort,
    string? SensorToken
);

public sealed record StudioWindowInfo(
    long Handle,
    bool IsVisible,
    string WindowState,
    string Visibility,
    int RectLeft,
    int RectTop,
    int RectRight,
    int RectBottom,
    int Width,
    int Height,
    bool IsWithinMonitor,
    bool IsResponding
);

public sealed record StudioStatusInfo(
    int StudioPid,
    bool IsRunning,
    string? ActiveTargetName,
    string? ActiveTargetPath,
    string? ActiveRunnerPath,
    int? TargetPid,
    int? SensorPort,
    string? EmbeddingMode,
    VisionTier ActiveVisionTier,
    bool IsSensorConnected,
    StudioWindowInfo? Window = null,
    int TargetWidth = 1280,
    int TargetHeight = 720,
    int PanelWidth = 0,
    int PanelHeight = 0
);

public sealed record RecentTargetEntry(
    string Name,
    string TargetPath,
    string? RunnerPath,
    string? ExtraArguments,
    DateTimeOffset LastUsedAtUtc
);
