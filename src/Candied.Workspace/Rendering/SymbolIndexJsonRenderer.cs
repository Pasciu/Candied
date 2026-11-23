using System.Collections.Generic;
using System.Text.Json;
using Candied.Core.Models;

namespace Candied.Workspace.Rendering;

/// <summary>
/// Pure template renderer that serializes an overview and namespace structure
/// into a compact, hierarchical JSON index for AI agents and automated tools.
/// </summary>
internal static class SymbolIndexJsonRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string Render(GameArchitectureOverview overview)
    {
        var namespaceDict = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var ns in overview.Namespaces)
        {
            var key = string.IsNullOrWhiteSpace(ns.NamespaceName) ? "Global" : ns.NamespaceName;
            namespaceDict[key] = ns.KeyTypes;
        }

        var payload = new
        {
            gameName = overview.GameName,
            unityVersion = overview.UnityVersion,
            runtimeKind = overview.RuntimeKind,
            targetFramework = overview.TargetFramework,
            totalTypes = overview.TotalTypesIndexed,
            totalMethods = overview.TotalMethodsIndexed,
            keyManagers = overview.KeyManagers,
            namespaces = namespaceDict
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
