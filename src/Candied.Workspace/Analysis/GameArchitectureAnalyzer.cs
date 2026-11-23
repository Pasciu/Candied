using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Candied.Core.Models;

namespace Candied.Workspace.Analysis;

/// <summary>
/// Analyzes detected game environments and extracted symbols to build
/// a comprehensive GameArchitectureOverview.
/// </summary>
internal static class GameArchitectureAnalyzer
{
    private static readonly string[] ManagerSuffixes =
    [
        "Manager",
        "Controller",
        "System",
        "Handler",
        "Service"
    ];

    public static GameArchitectureOverview Analyze(
        GameEnvironment gameEnv,
        IReadOnlyList<SymbolSummary> symbols)
    {
        ArgumentNullException.ThrowIfNull(gameEnv);
        symbols ??= [];

        // 1. Assemblies Overview
        var assemblies = new List<GameAssemblyOverview>();
        foreach (var path in gameEnv.GameAssemblyPaths)
        {
            var asmName = Path.GetFileNameWithoutExtension(path);
            var fileName = Path.GetFileName(path);
            long sizeBytes = 0;

            try
            {
                if (File.Exists(path))
                {
                    sizeBytes = new FileInfo(path).Length;
                }
            }
            catch
            {
                // Non-critical file size lookup failure
            }

            var isGameLogic = !asmName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) &&
                              !asmName.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
                              !asmName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase);

            assemblies.Add(new GameAssemblyOverview(asmName, fileName, sizeBytes, isGameLogic));
        }

        // 2. Class & Interface Types
        var typeSymbols = symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Struct)
            .ToList();

        var methodCount = symbols.Count(s => s.Kind == SymbolKind.Method);

        // 3. Key Managers Detection
        var keyManagers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in typeSymbols)
        {
            var shortName = type.FullName.Contains('.', StringComparison.Ordinal)
                ? type.FullName[(type.FullName.LastIndexOf('.') + 1)..]
                : type.FullName;

            if (ManagerSuffixes.Any(suffix => shortName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                keyManagers.Add(type.FullName);
            }
        }

        var sortedManagers = keyManagers.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();

        // 4. Namespace Grouping
        var namespaces = new List<GameNamespaceOverview>();
        var groupedByNs = typeSymbols
            .GroupBy(s => s.Namespace ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedByNs)
        {
            var allTypesInNs = group.Select(s => s.FullName).Distinct().ToList();
            var keyTypes = allTypesInNs.Take(15).ToList();

            namespaces.Add(new GameNamespaceOverview(
                NamespaceName: group.Key,
                KeyTypes: keyTypes,
                TotalTypesCount: allTypesInNs.Count
            ));
        }

        var runtimeKind = gameEnv.EngineType == UnityEngineType.Mono ? "Mono" : "IL2CPP";
        var targetFramework = "netstandard2.1";

        return new GameArchitectureOverview(
            GameName: gameEnv.GameName,
            UnityVersion: "Unity (Auto-detected)",
            RuntimeKind: runtimeKind,
            TargetFramework: targetFramework,
            Assemblies: assemblies,
            KeyManagers: sortedManagers,
            Namespaces: namespaces,
            TotalTypesIndexed: typeSymbols.Count,
            TotalMethodsIndexed: methodCount
        );
    }
}
