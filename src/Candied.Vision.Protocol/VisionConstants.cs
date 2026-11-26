namespace Candied.Vision.Protocol;

public static class VisionConstants
{
    public const string ProtocolVersion = "2.0.0";
    public const string SensorVersion = "2.0.0";

    public const string RouteHello = "/hello";
    public const string RouteCapabilities = "/capabilities";
    public const string RouteScreenshot = "/screenshot";
    public const string RouteUiElements = "/ui/elements";
    public const string RouteInput = "/input";
    public const string RouteWaitIdle = "/wait-idle";
    public const string RouteResize = "/window/resize";
    public const string RouteScene = "/scene";
    public const string RouteSceneNodes = "/scene/objects";
    public const string RouteLoadedAssets = "/assets/loaded";

    public const string HeaderToken = "X-Candied-Token";
    public const string HeaderUniformImage = "X-Candied-Uniform-Image";

    public const string EnvPort = "CANDIED_PORT";
    public const string EnvToken = "CANDIED_TOKEN";

    // Standard Capabilities
    public const string CapScreenshot = "screenshot";
    public const string CapUiElements = "ui_elements";
    public const string CapSceneGraph = "scene_graph";
    public const string CapRuntimeAssets = "runtime_assets";
    public const string CapWaitIdle = "wait_idle";
    public const string CapPointer = "pointer";
    public const string CapPointerIsolated = "pointer_isolated";
    public const string CapKeyboard = "keyboard";
    public const string CapGamepad = "gamepad";

    // Input Channels
    public const string ChannelPointer = "pointer";
    public const string ChannelKeyboard = "keyboard";
    public const string ChannelGamepad = "gamepad";
}
