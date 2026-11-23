using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

/// <summary>
/// Pure template renderer for Patches/README.md.
/// Explains the implementation details of gameplay patches, hook mechanics, and code architecture.
/// </summary>
internal static class PatchesReadmeTemplateRenderer
{
    public static string Render(ModScaffoldSpec spec)
    {
        return $$"""
# {{spec.ModName}} - Gameplay Patches Implementation Guide

This directory contains all runtime behavioral modifications and Harmony hooks for the mod.

## Architecture & Hook Mechanics

The mod intercepts game execution dynamically at runtime using HarmonyX:

1. **Target Method Interception (`[HarmonyPatch]`):**
   Each patch class specifies the target class and method in the game code to hook, for example:
   ```csharp
   [HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]
   ```

2. **Prefix Hooks (`[HarmonyPrefix]`):**
   - Executed **before** the original game method runs.
   - Used to inspect or alter incoming method arguments.
   - Return `true` to allow the original game method to proceed, or `false` to suppress and skip original execution.

3. **Postfix Hooks (`[HarmonyPostfix]`):**
   - Executed **after** the original game method completes.
   - Used to inspect or modify the return value (`ref __result`) or perform secondary reactions.

4. **Publicized Access:**
   - All game assemblies are publicized by MSBuild at compile time (`BepInEx.AssemblyPublicizer.MSBuild`).
   - Private and internal fields/methods of the game can be read and assigned directly without expensive reflection.

## Code Structure & Traceability

- `ExamplePatch.cs`: Reference implementation demonstrating the standard patch structure.
- When adding new features, create dedicated patch files named after the game feature or manager they target (e.g., `PlayerSpeedPatch.cs`, `ShopInventoryPatch.cs`).

## Best Practices for Patch Implementation

1. **High Cohesion:** Keep each patch file focused on a single game class or distinct feature.
2. **Non-Destructive Hooks:** Prefer Postfix alterations or lightweight Prefixes over completely suppressing original methods.
3. **Diagnostics:** Log informational milestones and warnings using `Plugin.Log` (e.g. `Plugin.Log.LogInfo("...")`) for live monitoring with `logs`.
""";
    }
}
