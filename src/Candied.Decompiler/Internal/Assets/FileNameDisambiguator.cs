using System;
using System.IO;

namespace Candied.Decompiler.Internal.Assets;

internal static class FileNameDisambiguator
{
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    public static string GetDisambiguatedFilePath(
        string directory,
        string baseName,
        string extension,
        long pathId,
        bool disambiguateCollisions = true)
    {
        var safeBaseName = SanitizeFileName(baseName);
        var cleanExt = extension.TrimStart('.');

        if (!disambiguateCollisions)
        {
            return Path.Combine(directory, $"{safeBaseName}.{cleanExt}");
        }

        var candidate = Path.Combine(directory, $"{safeBaseName}.{cleanExt}");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        // Suffix with PathId to guarantee deterministic unique naming
        var withPathId = Path.Combine(directory, $"{safeBaseName}_{pathId}.{cleanExt}");
        if (!File.Exists(withPathId))
        {
            return withPathId;
        }

        // Fallback with incremental counter
        int counter = 1;
        while (true)
        {
            var withCounter = Path.Combine(directory, $"{safeBaseName}_{pathId}_{counter}.{cleanExt}");
            if (!File.Exists(withCounter))
            {
                return withCounter;
            }
            counter++;
        }
    }
}
