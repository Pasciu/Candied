namespace Candied.VisionBridge.Protocol;

public static class VisionConstants
{
    public const string ProtocolVersion = "1.0.0";
    public const string PluginVersion = "1.0.0";
    public const string PluginGuid = "com.candied.visionbridge";
    public const string PluginName = "Candied.VisionBridge";

    public const string RouteHello = "/hello";
    public const string RouteCapabilities = "/capabilities";
    public const string RouteScreenshot = "/screenshot";
    public const string RouteUiElements = "/ui/elements";
    public const string RouteClick = "/ui/click";
    public const string RouteInputClick = "/input/click";
    public const string RouteWaitIdle = "/wait-idle";
    public const string RouteResize = "/window/resize";
    public const string RouteScene = "/scene";
    public const string RouteSceneObjects = "/scene/objects";
    public const string RouteLoadedAssets = "/assets/loaded";

    public const string HeaderToken = "X-Candied-Token";
    public const string HeaderUniformImage = "X-Candied-Uniform-Image";

    public const string EnvPort = "CANDIED_PORT";
    public const string EnvToken = "CANDIED_TOKEN";
}
