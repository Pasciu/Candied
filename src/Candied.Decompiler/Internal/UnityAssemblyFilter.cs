using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Candied.Core.Models;

namespace Candied.Decompiler.Internal;

internal static class UnityAssemblyFilter
{
    private static readonly HashSet<string> ExactExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mscorlib",
        "netstandard",
        "System",
        "WindowsBase",
        "mcs",
        "Newtonsoft.Json",
        "DOTween",
        "DOTween43",
        "DOTween46",
        "DOTween50",
        "DOTweenPro",
        "TextMeshPro",
        "Cinemachine",
        "Steamworks.NET",
        "InControl",
        "Rewired",
        "Rewired_Core",
        "Rewired_Windows",
        "FMOD",
        "FMODUnity",
        "zlib.net",
        "ExCSS.Unity",
        "AstarPathfindingProject",
        "OdinSerializer",
        "NavMeshComponents",
        "I18N",
        "BigIntegerLibrary",
        "DarkScreenSystem",
        "Equipment",
        "NavigationButtons",
        "PathSystem",
        "PoolSystem",
        "zxing.unity",
        "ThirdParty.DOTween"
    };

    private static readonly string[] ExcludedPrefixes =
    [
        "System.",
        "Microsoft.",
        "Mono.",
        "UnityEngine",
        "Unity.",
        "com.unity.",
        "Boo.",
        "UnityScript.",
        "I18N.",
        "ICSharpCode.",
        "Sirenix.",
        "NaughtyAttributes.",
        "Facepunch.",
        "Ak.Wwise.",
        "Amplify",
        "BouncyCastle.",
        "Pathfinding.",
        "ThirdParty.",
        "zxing.",
        "QFSW.",
        "IngameDebugConsole"
    ];

    public static bool IsExcludedAssembly(string assemblyNameOrPath)
    {
        var rawName = Path.GetFileName(assemblyNameOrPath);
        var name = rawName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                   rawName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? rawName[..rawName.LastIndexOf('.')]
            : rawName;

        if (ExactExcludedNames.Contains(name))
        {
            return true;
        }

        if (name.Contains(".ConverterFrom", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("ConverterFrom", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var prefix in ExcludedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static DiscoveredAssembly ClassifyAssembly(
        string fullPath,
        string? gameName = null,
        bool isDecompiled = false)
    {
        var rawName = Path.GetFileName(fullPath);
        var fileName = rawName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                       rawName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? rawName[..rawName.LastIndexOf('.')]
            : rawName;

        long size = 0;
        try
        {
            var fi = new FileInfo(fullPath);
            if (fi.Exists)
            {
                size = fi.Length;
            }
        }
        catch
        {
            // Ignore file read error on size probing
        }

        if (IsExcludedAssembly(fileName))
        {
            return new DiscoveredAssembly(
                rawName,
                fullPath,
                size,
                AssemblyTier.AuxiliaryOrThirdParty,
                "Excluded BCL, Unity engine, or standard middleware library",
                isDecompiled);
        }

        if (fileName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase))
        {
            return new DiscoveredAssembly(
                rawName,
                fullPath,
                size,
                AssemblyTier.CoreGame,
                "Primary Unity gameplay assembly",
                isDecompiled);
        }

        var normalizedGameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim();
        var compactGameName = normalizedGameName != null
            ? new string(normalizedGameName.Where(char.IsLetterOrDigit).ToArray())
            : null;

        var compactFileName = new string(fileName.Where(char.IsLetterOrDigit).ToArray());
        if ((normalizedGameName != null && fileName.StartsWith(normalizedGameName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(compactGameName) && compactFileName.StartsWith(compactGameName, StringComparison.OrdinalIgnoreCase)))
        {
            return new DiscoveredAssembly(
                rawName,
                fullPath,
                size,
                AssemblyTier.CoreGame,
                $"Matches game prefix '{gameName}'",
                isDecompiled);
        }

        return new DiscoveredAssembly(
            rawName,
            fullPath,
            size,
            AssemblyTier.GameModular,
            "Game-specific modular or third-party logic assembly",
            isDecompiled);
    }

    public static IReadOnlyList<DiscoveredAssembly> ClassifyAssemblies(
        IEnumerable<string> assemblyPaths,
        string? gameName = null,
        Func<string, bool>? isDecompiledChecker = null)
    {
        var inputList = assemblyPaths.ToList();
        if (inputList.Count == 0)
        {
            return Array.Empty<DiscoveredAssembly>();
        }

        var classified = new List<DiscoveredAssembly>();
        foreach (var path in inputList)
        {
            var fileName = Path.GetFileName(path);
            var isDecompiled = isDecompiledChecker?.Invoke(fileName) ?? false;
            classified.Add(ClassifyAssembly(path, gameName, isDecompiled));
        }

        // Order: CoreGame first, then GameModular, then AuxiliaryOrThirdParty
        return classified
            .OrderBy(a => a.Tier)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> FilterGameAssemblies(
        IEnumerable<string> assemblyPaths,
        string? gameName = null)
    {
        var inputList = assemblyPaths.ToList();
        if (inputList.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        string? primaryAssembly = null;
        string? firstpassAssembly = null;
        var gameNamedAssemblies = new List<string>();
        var otherCandidates = new List<string>();

        var normalizedGameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim();
        var compactGameName = normalizedGameName != null
            ? new string(normalizedGameName.Where(char.IsLetterOrDigit).ToArray())
            : null;

        foreach (var path in inputList)
        {
            var rawName = Path.GetFileName(path);
            var fileName = rawName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                           rawName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? rawName[..rawName.LastIndexOf('.')]
                : rawName;

            if (IsExcludedAssembly(fileName))
            {
                continue;
            }

            if (fileName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
            {
                primaryAssembly = path;
                continue;
            }

            if (fileName.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase))
            {
                firstpassAssembly = path;
                continue;
            }

            // If gameName is known and the assembly starts with gameName (either raw or compact alphanumeric):
            var compactFileName = new string(fileName.Where(char.IsLetterOrDigit).ToArray());
            if ((normalizedGameName != null && fileName.StartsWith(normalizedGameName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(compactGameName) && compactFileName.StartsWith(compactGameName, StringComparison.OrdinalIgnoreCase)))
            {
                gameNamedAssemblies.Add(path);
                continue;
            }

            otherCandidates.Add(path);
        }

        if (primaryAssembly != null)
        {
            result.Add(primaryAssembly);
        }

        if (firstpassAssembly != null)
        {
            result.Add(firstpassAssembly);
        }

        // Sort game-named assemblies by filename
        gameNamedAssemblies.Sort((a, b) => string.Compare(
            Path.GetFileName(a),
            Path.GetFileName(b),
            StringComparison.OrdinalIgnoreCase));
        result.AddRange(gameNamedAssemblies);

        // Sort other candidates by filename
        otherCandidates.Sort((a, b) => string.Compare(
            Path.GetFileName(a),
            Path.GetFileName(b),
            StringComparison.OrdinalIgnoreCase));
        result.AddRange(otherCandidates);

        // Fallback: If all assemblies were excluded, return the original collection to avoid empty lists
        if (result.Count == 0)
        {
            return inputList;
        }

        return result;
    }
}
