using System;
using System.IO;

namespace Candied.Workspace;

/// <summary>
/// Resolves paths for Candied user configuration and ephemeral session states stored outside the repository.
/// </summary>
internal static class CandiedUserConfigPaths
{
    public static string GetConfigDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("CANDIED_USER_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return overrideDir;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(appData, "Candied");
    }

    public static string GetRegisteredGamesPath() =>
        Path.Combine(GetConfigDirectory(), "registered_games.json");

    public static string GetStudioSessionPath() =>
        Path.Combine(GetConfigDirectory(), "studio_session.json");
}
