using System.Text;

namespace Candied.Core.Models;

/// <summary>
/// Pure Three-Zone storage path layout for Candied (zero System.IO dependencies).
/// Isolates Zone 1 (Target Game/ROM), Zone 2 (Mod Source Workspace), and Zone 3 (Candied Artifacts &amp; Indexes).
/// </summary>
public sealed record CandiedStorageLayout(string WorkspaceRoot)
{
    public string CandiedRoot => Combine(WorkspaceRoot, ".candied");
    public string ModsRoot => Combine(WorkspaceRoot, "mods");
    public string CacheRoot => Combine(CandiedRoot, "cache");

    public string GetLoaderCacheDir(string loaderId) =>
        Combine(CacheRoot, Sanitize(loaderId));

    public string GetTargetArtifactsDir(string targetName) =>
        Combine(CandiedRoot, "games", Sanitize(targetName));

    public string GetExtractedCodeDir(string targetName, string? moduleName = null) =>
        string.IsNullOrWhiteSpace(moduleName)
            ? Combine(GetTargetArtifactsDir(targetName), "extracted")
            : Combine(GetTargetArtifactsDir(targetName), "extracted", Sanitize(moduleName));

    public string GetDatabasePath(string targetName) =>
        Combine(GetTargetArtifactsDir(targetName), "index.db");

    public string GetDocsDir(string targetName) =>
        Combine(GetTargetArtifactsDir(targetName), "docs");

    public string GetOverviewDocPath(string targetName) =>
        Combine(GetDocsDir(targetName), "TARGET_OVERVIEW.md");

    public string GetNamespacesDocPath(string targetName) =>
        Combine(GetDocsDir(targetName), "NAMESPACES.md");

    public string GetSymbolsIndexJsonPath(string targetName) =>
        Combine(GetDocsDir(targetName), "SYMBOLS_INDEX.json");

    public string GetResearchNotesPath(string targetName) =>
        Combine(GetDocsDir(targetName), "RESEARCH.md");

    public string GetControlProfilePath(string targetName) =>
        Combine(GetTargetArtifactsDir(targetName), "controls.json");

    public string GetScreenshotsDir(string targetName) =>
        Combine(GetTargetArtifactsDir(targetName), "screenshots");

    public string GetAssetsDir(string targetName) =>
        Combine(GetTargetArtifactsDir(targetName), "assets");

    public string GetModProjectDir(string modName) =>
        Combine(ModsRoot, Sanitize(modName));

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
