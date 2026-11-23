using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

internal static class PluginTemplateRenderer
{
    public static string Render(ModScaffoldSpec spec)
    {
        var version = string.IsNullOrWhiteSpace(spec.Version) ? "1.0.0" : spec.Version;

        return $$"""
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace {{spec.ModName}};

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "{{spec.PluginGuid.Value}}";
    public const string PluginName = "{{spec.ModName}}";
    public const string PluginVersion = "{{version}}";

    internal static ManualLogSource Log { get; private set; } = null!;
    private Harmony? _harmony;

    private void Awake()
    {
        Log = Logger;
        Log.LogInfo($"Plugin {PluginName} (v{PluginVersion}) loaded successfully.");

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
""";
    }
}
