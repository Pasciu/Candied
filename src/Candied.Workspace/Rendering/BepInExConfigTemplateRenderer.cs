namespace Candied.Workspace.Rendering;

/// <summary>
/// Pure template renderer for generating BepInEx default configuration files.
/// </summary>
internal static class BepInExConfigTemplateRenderer
{
    public static string RenderDefaultConfig(bool enableConsole = true)
    {
        var consoleFlag = enableConsole ? "true" : "false";
        return $"""
[Logging.Console]
Enabled = {consoleFlag}
ShiftCISCP = 65001
StandardOutType = Auto

[Logging.Disk]
AppendLog = false
Enabled = true
IncludePreloader = true
WriteUnityLog = true

[Preloader]
ApplyRuntimePatches = true
DumpAssemblies = false
LoadRuntimeAssemblies = true
""";
    }
}
