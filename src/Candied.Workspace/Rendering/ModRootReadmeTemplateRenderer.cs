using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

/// <summary>
/// Pure template renderer for the mod root README.md.
/// Provides a natural language overview of the mod and player experience without source code references.
/// </summary>
internal static class ModRootReadmeTemplateRenderer
{
    public static string Render(ModScaffoldSpec spec)
    {
        var assetSection = spec.IncludeAssetPipeline
            ? "\n- **`Assets/`:** Contains external visual assets, sprites, and custom textures. For information on asset formats and disk loading, consult `Assets/README.md`."
            : string.Empty;

        return $$"""
# {{spec.ModName}}

A gameplay modification for **{{spec.TargetGame.GameName}}**.

## Overview

This mod enhances **{{spec.TargetGame.GameName}}** by introducing custom gameplay adjustments, quality-of-life enhancements, and tailored player mechanics. It runs seamlessly on top of the BepInEx modding framework, providing a smooth, non-intrusive player experience.

## Features & Player Experience

- **Seamless Integration:** Designed to integrate naturally into existing save files and ongoing playthroughs.
- **Configurable Mechanics:** Modifies targeted gameplay rules to provide a more engaging and balanced experience.
- **Safe & Reversible:** Operates cleanly through the mod loader without permanently altering original game files.

## Project Structure

This modification is organized into dedicated functional areas:

- **Core Project:** Defines the mod's identity, versioning, and lifecycle management.
- **`Patches/`:** Contains the game hooks and runtime behavioral adjustments. For detailed technical explanations of each hook, consult the guide in `Patches/README.md`.{{assetSection}}

## Requirements

- **Target Game:** {{spec.TargetGame.GameName}}
- **Mod Loader:** BepInEx 5.4 or newer
- **Engine Runtime:** Unity ({{spec.TargetGame.EngineType}})
""";
    }
}
