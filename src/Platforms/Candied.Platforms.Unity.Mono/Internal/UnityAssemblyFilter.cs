using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Candied.Core.Models;

namespace Candied.Platforms.Unity.Mono.Internal;

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
        string rawName = Path.GetFileName(assemblyNameOrPath);
        string name = rawName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
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

    public static DiscoveredCodeModule ClassifyModule(
        string fullPath,
        string? gameName = null,
        bool isExtracted = false)
    {
        string rawName = Path.GetFileName(fullPath);
        string fileName = rawName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
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
        }

        if (IsExcludedAssembly(fileName))
        {
            return new DiscoveredCodeModule(
                rawName,
                fullPath,
                size,
                CodeModuleTier.AuxiliaryOrThirdParty,
                "Excluded BCL, Unity engine, or standard middleware library",
                isExtracted);
        }

        if (fileName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase))
        {
            return new DiscoveredCodeModule(
                rawName,
                fullPath,
                size,
                CodeModuleTier.CoreLogic,
                "Primary Unity gameplay assembly",
                isExtracted);
        }

        string? normalizedGameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim();
        string? compactGameName = normalizedGameName != null
            ? new string(normalizedGameName.Where(char.IsLetterOrDigit).ToArray())
            : null;

        string compactFileName = new string(fileName.Where(char.IsLetterOrDigit).ToArray());
        if ((normalizedGameName != null && fileName.StartsWith(normalizedGameName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(compactGameName) && compactFileName.StartsWith(compactGameName, StringComparison.OrdinalIgnoreCase)))
        {
            return new DiscoveredCodeModule(
                rawName,
                fullPath,
                size,
                CodeModuleTier.CoreLogic,
                $"Matches target prefix '{gameName}'",
                isExtracted);
        }

        return new DiscoveredCodeModule(
            rawName,
            fullPath,
            size,
            CodeModuleTier.ModularLogic,
            "Game-specific modular logic assembly",
            isExtracted);
    }

    public static IReadOnlyList<DiscoveredCodeModule> ClassifyModules(
        IEnumerable<string> assemblyPaths,
        string? gameName = null,
        Func<string, bool>? isExtractedChecker = null)
    {
        var inputList = assemblyPaths.ToList();
        if (inputList.Count == 0)
        {
            return Array.Empty<DiscoveredCodeModule>();
        }

        var classified = new List<DiscoveredCodeModule>(inputList.Count);
        foreach (var path in inputList)
        {
            string fileName = Path.GetFileName(path);
            bool isExtracted = isExtractedChecker?.Invoke(fileName) ?? false;
            classified.Add(ClassifyModule(path, gameName, isExtracted));
        }

        return classified
            .OrderBy(a => a.Tier)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
