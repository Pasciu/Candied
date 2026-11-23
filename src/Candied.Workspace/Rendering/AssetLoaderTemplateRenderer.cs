using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

internal static class AssetLoaderTemplateRenderer
{
    public static string Render(ModScaffoldSpec spec)
    {
        return $$"""
using System;
using System.IO;
using UnityEngine;

namespace {{spec.ModName}};

/// <summary>
/// Utility for loading loose assets (such as PNG sprites and textures) from the mod plugin folder.
/// </summary>
public static class AssetLoader
{
    private static string? s_modDirectory;

    /// <summary>
    /// Gets the absolute directory where this mod plugin is located.
    /// </summary>
    public static string ModDirectory
    {
        get
        {
            if (s_modDirectory != null)
            {
                return s_modDirectory;
            }

            var location = typeof(AssetLoader).Assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                var dir = Path.GetDirectoryName(location);
                if (!string.IsNullOrEmpty(dir))
                {
                    s_modDirectory = dir;
                    return s_modDirectory;
                }
            }

            s_modDirectory = Path.Combine(BepInEx.Paths.PluginPath, "{{spec.ModName}}");
            return s_modDirectory;
        }
    }

    /// <summary>
    /// Gets the absolute directory for mod assets.
    /// </summary>
    public static string AssetsDirectory => Path.Combine(ModDirectory, "Assets");

    /// <summary>
    /// Resolves an asset path relative to the Assets directory or the mod directory.
    /// </summary>
    public static string? ResolveAssetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        // 1. Try relative to AssetsDirectory (e.g. "character.png" -> "<Mod>/Assets/character.png")
        var assetsPath = Path.Combine(AssetsDirectory, relativePath);
        if (File.Exists(assetsPath))
        {
            return assetsPath;
        }

        // 2. Try relative to ModDirectory (e.g. "Assets/character.png" -> "<Mod>/Assets/character.png")
        var modPath = Path.Combine(ModDirectory, relativePath);
        if (File.Exists(modPath))
        {
            return modPath;
        }

        return assetsPath;
    }

    /// <summary>
    /// Loads a Sprite from a loose file in the mod folder (e.g. "Assets/character.png" or "character.png").
    /// </summary>
    public static Sprite? LoadSprite(string relativePath, float pixelsPerUnit = 100f)
    {
        var filePath = ResolveAssetPath(relativePath);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(filePath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes))
        {
            return null;
        }

        return Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit);
    }

    /// <summary>
    /// Loads a Texture2D from a loose file in the mod folder (e.g. "Assets/texture.png" or "texture.png").
    /// </summary>
    public static Texture2D? LoadTexture(string relativePath)
    {
        var filePath = ResolveAssetPath(relativePath);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(filePath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes))
        {
            return null;
        }

        return texture;
    }

    /// <summary>
    /// Backward-compatible alias for LoadSprite.
    /// </summary>
    [Obsolete("Use LoadSprite to load loose assets from the plugin directory.")]
    public static Sprite? LoadEmbeddedSprite(string relativePath, float pixelsPerUnit = 100f)
        => LoadSprite(relativePath, pixelsPerUnit);
}
""";
    }
}
