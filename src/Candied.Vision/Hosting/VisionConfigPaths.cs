using System;
using System.IO;
using System.Text;

namespace Candied.Vision.Hosting;

public static class VisionConfigPaths
{
    public static string GetCandiedAppDataDirectory()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("CANDIED_USER_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return overrideDir;
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(appData, "Candied");
    }

    public static string GetStudioSessionPath() =>
        Path.Combine(GetCandiedAppDataDirectory(), "studio_session.json");

    public static string GetRecentTargetsPath() =>
        Path.Combine(GetCandiedAppDataDirectory(), "recent_targets.json");

    public static string GetLogsDirectory()
    {
        string logDir = Path.Combine(GetCandiedAppDataDirectory(), "logs");
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        return logDir;
    }

    public static string GetStudioLogPath() =>
        Path.Combine(GetLogsDirectory(), "studio.log");

    public static string ResolveControlProfilePath(string targetName)
    {
        string sanitized = SanitizeSegment(targetName);
        string? workspaceRoot = TryFindWorkspaceRoot();
        if (!string.IsNullOrEmpty(workspaceRoot))
        {
            return Path.Combine(workspaceRoot, ".candied", "games", sanitized, "controls.json");
        }

        return Path.Combine(GetCandiedAppDataDirectory(), "controls", sanitized, "controls.json");
    }

    public static string SanitizeSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Default";
        }

        var sb = new StringBuilder();
        foreach (char c in name.Trim())
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

    private static string? TryFindWorkspaceRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "Candied.sln")) ||
                Directory.Exists(Path.Combine(dir, ".candied")) ||
                Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        string cwd = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(cwd, "Candied.sln")) || Directory.Exists(Path.Combine(cwd, ".candied")))
        {
            return cwd;
        }

        return null;
    }
}
