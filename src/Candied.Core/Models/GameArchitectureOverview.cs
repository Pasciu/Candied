using System.Collections.Generic;

namespace Candied.Core.Models;

/// <summary>
/// Summary of an identified game or framework assembly.
/// </summary>
public record GameAssemblyOverview(
    string AssemblyName,
    string RelativePath,
    long SizeBytes,
    bool IsGameLogic
);

/// <summary>
/// Summary of a namespace and its prominent types.
/// </summary>
public record GameNamespaceOverview(
    string NamespaceName,
    IReadOnlyList<string> KeyTypes,
    int TotalTypesCount
);

/// <summary>
/// High-level architectural overview of a decompiled and indexed game.
/// Used to generate GAME_OVERVIEW.md and assist AI and developers with instant domain knowledge.
/// </summary>
public record GameArchitectureOverview(
    string GameName,
    string UnityVersion,
    string RuntimeKind,
    string TargetFramework,
    IReadOnlyList<GameAssemblyOverview> Assemblies,
    IReadOnlyList<string> KeyManagers,
    IReadOnlyList<GameNamespaceOverview> Namespaces,
    int TotalTypesIndexed,
    int TotalMethodsIndexed
);
