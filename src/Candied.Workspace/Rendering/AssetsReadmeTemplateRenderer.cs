using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

/// <summary>
/// Pure template renderer for Assets/README.md.
/// Explains the implementation details of loose assets, runtime disk loading, and folder structure.
/// </summary>
internal static class AssetsReadmeTemplateRenderer
{
    public static string Render(ModScaffoldSpec spec)
    {
        return $$"""
# {{spec.ModName}} - Loose Assets & Resources Guide

This directory contains loose graphical and audio resources utilized by the mod.

## Architecture & Build Deployment

Unlike traditional mods that embed resources inside the assembly DLL, this project uses a **loose asset pipeline**:

1. **No DLL Bloat:** Assets remain external files on disk, making them easy to inspect, customize, and replace without recompilation.
2. **Automatic Deployment:** On `dotnet build`, MSBuild automatically copies all files and subdirectories from this `Assets/` folder to:
   `<GameRoot>/BepInEx/plugins/{{spec.ModName}}/Assets/`

## Runtime Loading (`AssetLoader.cs`)

The mod project includes `AssetLoader.cs` which provides safe, disk-based loading methods:

- `AssetLoader.LoadSprite("filename.png")`: Reads the file from disk, decodes it into a Unity `Texture2D`, and constructs a `Sprite`.
- `AssetLoader.LoadTexture("filename.png")`: Loads a raw `Texture2D` from disk.
- Paths can be relative to `Assets/` (e.g. `"icon.png"`) or relative to the mod directory (e.g. `"Assets/icon.png"`).

## Supported File Formats & Organization

- **Textures / Sprites:** `.png`, `.jpg`, `.jpeg` (RGBA32 format recommended for transparency).
- **Subdirectories:** You can organize assets into subfolders (e.g., `Assets/UI/`, `Assets/Items/`, `Assets/Textures/`). MSBuild preserves subfolder hierarchies upon deployment.
""";
    }
}
