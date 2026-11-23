using System;

namespace Candied.Core.Models;

public record VisionScreenshot(
    byte[] PngBytes,
    bool IsUniform,
    int Width,
    int Height
);

public record VisionBridgeError(
    string Code,
    string Message,
    string Suggestion
);

public record SceneObjectsQuery(
    string? NameFilter = null,
    bool IncludeInactive = false,
    bool VisibleOnly = false,
    string? SortingLayer = null,
    int MaxResults = 100
);

public record RuntimeAssetsQuery(
    string? NameFilter = null,
    string? AssetType = null,
    int MaxResults = 50
);
