using System.Text;

namespace Candied.Core.Models;

/// <summary>
/// Defines the strict file storage layout for Candied.
/// Enforces complete isolation between the Game Installation (read-only),
/// the Mod Source Project (version-controlled), and Candied Artifacts (local/ignored).
/// </summary>
public sealed record CandiedStorageLayout(string WorkspaceRoot)
{
    public string CandiedRoot => Combine(WorkspaceRoot, ".candied");
    public string ModsRoot => Combine(WorkspaceRoot, "mods");
    public string CacheRoot => Combine(CandiedRoot, "cache");
    public string BepInExCacheDir => Combine(CacheRoot, "bepinex");

    /// <summary>
    /// Gets the root artifacts directory for a specific game: &lt;WorkspaceRoot&gt;/.candied/games/&lt;GameName&gt;/
    /// </summary>
    public string GetGameArtifactsDir(string gameName) =>
        Combine(CandiedRoot, "games", Sanitize(gameName));

    /// <summary>
    /// Gets the decompiled C# source files directory: &lt;WorkspaceRoot&gt;/.candied/games/&lt;GameName&gt;/decompiled/ or for a specific assembly.
    /// </summary>
    public string GetDecompiledDir(string gameName, string? assemblyName = null) =>
        string.IsNullOrWhiteSpace(assemblyName)
            ? Combine(GetGameArtifactsDir(gameName), "decompiled")
            : Combine(GetGameArtifactsDir(gameName), "decompiled", Sanitize(assemblyName));

    /// <summary>
    /// Gets the SQLite symbol index database path: &lt;WorkspaceRoot&gt;/.candied/games/&lt;GameName&gt;/index.db
    /// </summary>
    public string GetDatabasePath(string gameName) =>
        Combine(GetGameArtifactsDir(gameName), "index.db");

    /// <summary>
    /// Gets the documentation hub directory: &lt;WorkspaceRoot&gt;/.candied/games/&lt;GameName&gt;/docs/
    /// </summary>
    public string GetDocsDir(string gameName) =>
        Combine(GetGameArtifactsDir(gameName), "docs");

    /// <summary>
    /// Gets the path to GAME_OVERVIEW.md
    /// </summary>
    public string GetOverviewDocPath(string gameName) =>
        Combine(GetDocsDir(gameName), "GAME_OVERVIEW.md");

    /// <summary>
    /// Gets the path to NAMESPACES.md
    /// </summary>
    public string GetNamespacesDocPath(string gameName) =>
        Combine(GetDocsDir(gameName), "NAMESPACES.md");

    /// <summary>
    /// Gets the path to SYMBOLS_INDEX.json
    /// </summary>
    public string GetSymbolsIndexJsonPath(string gameName) =>
        Combine(GetDocsDir(gameName), "SYMBOLS_INDEX.json");

    /// <summary>
    /// Gets the mod project directory: &lt;WorkspaceRoot&gt;/mods/&lt;ModName&gt;/
    /// </summary>
    public string GetModProjectDir(string modName) =>
        Combine(ModsRoot, Sanitize(modName));

    /// <summary>
    /// Gets the IPC directory for live game inspection: &lt;WorkspaceRoot&gt;/.candied/games/&lt;GameName&gt;/ipc/
    /// </summary>
    public string GetIpcDir(string gameName) =>
        Combine(GetGameArtifactsDir(gameName), "ipc");

    /// <summary>
    /// Gets the assets directory for on-demand extracted sprites/textures: &lt;WorkspaceRoot&gt;/.candied/games/&lt;GameName&gt;/assets/
    /// </summary>
    public string GetAssetsDir(string gameName) =>
        Combine(GetGameArtifactsDir(gameName), "assets");

    public static string Combine(string first, string second)
    {
        var f = (first ?? string.Empty).TrimEnd('/', '\\');
        var s = (second ?? string.Empty).TrimStart('/', '\\');
        return $"{f}/{s}";
    }

    public static string Combine(string first, string second, string third) =>
        Combine(Combine(first, second), third);

    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Default";
        }

        var sb = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append('-');
            }
        }

        return sb.Length > 0 ? sb.ToString() : "Default";
    }
}
