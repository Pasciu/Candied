using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Candied.Core.Models;

namespace Candied.Infrastructure.Internal;

internal static class TargetArchitectureAnalyzer
{
    private static readonly string[] ManagerSuffixes =
    [
        "Manager",
        "Controller",
        "System",
        "Handler",
        "Service"
    ];

    public static TargetArchitectureOverview Analyze(
        TargetDescriptor target,
        IReadOnlyList<SymbolSummary> symbols)
    {
        ArgumentNullException.ThrowIfNull(target);
        symbols ??= [];

        var modules = target.CodeModules
            .Where(m => m.Tier != CodeModuleTier.AuxiliaryOrThirdParty)
            .Select(m => new CodeModuleOverview(
                ModuleName: m.Name,
                RelativePath: m.Name,
                SizeBytes: m.SizeBytes,
                IsCoreLogic: m.Tier == CodeModuleTier.CoreLogic))
            .ToList();

        var typeSymbols = symbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Struct)
            .ToList();

        int methodCount = symbols.Count(s => s.Kind == SymbolKind.Method);

        var keyManagers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in typeSymbols)
        {
            string shortName = type.FullName.Contains('.', StringComparison.Ordinal)
                ? type.FullName[(type.FullName.LastIndexOf('.') + 1)..]
                : type.FullName;

            if (ManagerSuffixes.Any(suffix => shortName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                keyManagers.Add(type.FullName);
            }
        }

        var namespaces = typeSymbols
            .GroupBy(s => s.Namespace ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var allTypes = g.Select(s => s.FullName).Distinct().ToList();
                return new NamespaceOverview(
                    NamespaceName: g.Key,
                    KeyTypes: allTypes.Take(15).ToList(),
                    TotalTypesCount: allTypes.Count);
            })
            .ToList();

        return new TargetArchitectureOverview(
            TargetName: target.Name,
            PlatformDisplayName: target.PlatformDisplayName,
            EngineVersion: target.EngineVersion ?? "Unknown",
            RuntimeFramework: target.RuntimeFramework ?? "Native",
            Modules: modules,
            KeyManagers: keyManagers.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList(),
            Namespaces: namespaces,
            TotalTypesIndexed: typeSymbols.Count,
            TotalMethodsIndexed: methodCount);
    }
}

internal static class KnowledgeHubRenderers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string RenderOverview(TargetArchitectureOverview overview)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Target Overview: {overview.TargetName}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Platform:** {overview.PlatformDisplayName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Engine Version:** {overview.EngineVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Runtime:** {overview.RuntimeFramework}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Total Types Indexed:** {overview.TotalTypesIndexed}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Total Methods Indexed:** {overview.TotalMethodsIndexed}");
        sb.AppendLine();

        sb.AppendLine("## Primary Code Modules");
        if (overview.Modules.Count == 0)
        {
            sb.AppendLine("- *(No primary code modules discovered)*");
        }
        else
        {
            foreach (var mod in overview.Modules)
            {
                string role = mod.IsCoreLogic ? "Core" : "Modular";
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{mod.ModuleName}` ({role}, {mod.SizeBytes} bytes)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Key Managers & Controllers");
        if (overview.KeyManagers.Count == 0)
        {
            sb.AppendLine("- *(None indexed yet)*");
        }
        else
        {
            foreach (var mgr in overview.KeyManagers.Take(50))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{mgr}`");
            }
        }

        return sb.ToString();
    }

    public static string RenderNamespaces(IReadOnlyList<NamespaceOverview> namespaces, string targetName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Namespaces & Modules: {targetName}");
        sb.AppendLine();

        foreach (var ns in namespaces)
        {
            string label = string.IsNullOrEmpty(ns.NamespaceName) ? "(Global)" : ns.NamespaceName;
            sb.AppendLine(CultureInfo.InvariantCulture, $"## `{label}` ({ns.TotalTypesCount} types)");
            foreach (var t in ns.KeyTypes)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{t}`");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string RenderSymbolsJson(TargetArchitectureOverview overview)
    {
        return JsonSerializer.Serialize(overview, JsonOptions);
    }
}
