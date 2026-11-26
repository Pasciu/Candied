using System.Collections.Generic;

namespace Candied.Vision.Protocol;

public sealed class VisionPointDto
{
    public float X { get; set; }
    public float Y { get; set; }

    public VisionPointDto()
    {
    }

    public VisionPointDto(float x, float y)
    {
        X = x;
        Y = y;
    }
}

public sealed class VisionRectDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public VisionRectDto()
    {
    }

    public VisionRectDto(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

public sealed class VisionHelloDto
{
    public string ProtocolVersion { get; set; } = VisionConstants.ProtocolVersion;
    public string SensorVersion { get; set; } = VisionConstants.SensorVersion;
    public string EngineName { get; set; } = string.Empty;
    public string EngineVariant { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
}

public sealed class VisionCapabilitiesDto
{
    public List<string> Capabilities { get; set; } = new List<string>();
    public string ScreenshotMethod { get; set; } = string.Empty;
    public string ScreenshotLimitations { get; set; } = string.Empty;
    public string PointerMethod { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

public sealed class VisionSceneInfoDto
{
    public string ActiveSceneName { get; set; } = string.Empty;
    public int ActiveSceneIndex { get; set; }
    public int RootCount { get; set; }
    public bool IsLoaded { get; set; }
    public int LoadedSceneCount { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int ActiveNodeCount { get; set; }
    public float ElapsedSeconds { get; set; }
}

public sealed class VisionUiElementDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public VisionRectDto PixelRect { get; set; } = new VisionRectDto();
    public VisionRectDto NormalizedRect { get; set; } = new VisionRectDto();
    public bool Interactable { get; set; }
    public bool Visible { get; set; }
}

public sealed class VisionUiElementsResponse
{
    public int Count { get; set; }
    public List<VisionUiElementDto> Elements { get; set; } = new List<VisionUiElementDto>();
}

public sealed class VisionInputRequest
{
    public string Channel { get; set; } = VisionConstants.ChannelPointer;
    public string Action { get; set; } = "click";
    public string? TargetElementId { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public int Button { get; set; }
    public string? Method { get; set; }
    public string? Key { get; set; }
    public List<string> Modifiers { get; set; } = new List<string>();
    public string? GamepadButton { get; set; }
    public string? Stick { get; set; }
    public float? StickX { get; set; }
    public float? StickY { get; set; }
    public int HoldFrames { get; set; } = 6;
}

public sealed class VisionInputResponse
{
    public bool Success { get; set; }
    public string Channel { get; set; } = VisionConstants.ChannelPointer;
    public string Action { get; set; } = string.Empty;
    public string? HitElementId { get; set; }
    public string? HitElementType { get; set; }
    public float HitPointX { get; set; }
    public float HitPointY { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class VisionWaitIdleResponse
{
    public bool IdleReached { get; set; }
    public int ElapsedFrames { get; set; }
    public long ElapsedMs { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class VisionSceneCameraDto
{
    public string Name { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public bool Orthographic { get; set; }
    public float OrthographicSize { get; set; }
    public int CullingMask { get; set; }
    public float Depth { get; set; }
}

public sealed class VisionSceneNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentName { get; set; }
    public string FullPath { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public bool ActiveSelf { get; set; } = true;
    public bool ActiveInHierarchy { get; set; } = true;
    public bool IsVisible { get; set; }
    public float[] LocalPosition { get; set; } = new float[3];
    public float[] LocalScale { get; set; } = new float[] { 1f, 1f, 1f };
    public float[] BoundsCenter { get; set; } = new float[3];
    public float[] BoundsExtents { get; set; } = new float[3];
    public List<string> Components { get; set; } = new List<string>();
    public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
}

public sealed class VisionSceneGraphResponse
{
    public bool Success { get; set; }
    public int TotalCameras { get; set; }
    public int TotalNodes { get; set; }
    public int TotalMatched { get; set; }
    public bool Truncated { get; set; }
    public List<VisionSceneCameraDto> Cameras { get; set; } = new List<VisionSceneCameraDto>();
    public List<VisionSceneNodeDto> Nodes { get; set; } = new List<VisionSceneNodeDto>();
    public string Message { get; set; } = string.Empty;
}

public sealed class VisionLoadedContainerDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsStreamedScene { get; set; }
    public int AssetCount { get; set; }
    public List<string> MatchingAssetPaths { get; set; } = new List<string>();
}

public sealed class VisionLoadedAssetDto
{
    public string Name { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
}

public sealed class VisionRuntimeAssetsResponse
{
    public bool Success { get; set; }
    public int TotalContainers { get; set; }
    public int TotalLoadedAssets { get; set; }
    public bool Truncated { get; set; }
    public List<VisionLoadedContainerDto> Containers { get; set; } = new List<VisionLoadedContainerDto>();
    public List<VisionLoadedAssetDto> LoadedAssets { get; set; } = new List<VisionLoadedAssetDto>();
    public string Message { get; set; } = string.Empty;
}

public sealed class VisionResizeRequest
{
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Fullscreen { get; set; }
}

public sealed class VisionResizeResponse
{
    public bool Success { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class VisionErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;

    public VisionErrorResponse()
    {
    }

    public VisionErrorResponse(string code, string message, string suggestion)
    {
        Code = code;
        Message = message;
        Suggestion = suggestion;
    }
}
