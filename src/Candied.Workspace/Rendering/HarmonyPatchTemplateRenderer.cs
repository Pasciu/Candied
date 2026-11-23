using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

internal static class HarmonyPatchTemplateRenderer
{
    public static string Render(ModScaffoldSpec spec)
    {
        return $$"""
using HarmonyLib;

namespace {{spec.ModName}}.Patches;

/// <summary>
/// Sample Harmony patch template demonstrating how to hook game methods.
/// To activate, uncomment the annotations and configure the target class and method.
/// </summary>
// [HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]
// internal static class TargetClassPatch
// {
//     [HarmonyPrefix]
//     private static bool Prefix()
//     {
//         Plugin.Log.LogInfo("TargetClass.TargetMethod Prefix executed!");
//         return true; // Return true to continue original method execution
//     }
//
//     [HarmonyPostfix]
//     private static void Postfix()
//     {
//         Plugin.Log.LogInfo("TargetClass.TargetMethod Postfix executed!");
//     }
// }
""";
    }
}
